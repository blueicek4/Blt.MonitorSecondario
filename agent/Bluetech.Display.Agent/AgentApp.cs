using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Logging;
using Bluetech.Display.Agent.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Bluetech.Display.Agent;

/// <summary>
/// L'applicazione: il servizio che legge le casse e il server per i display. Tre pagine:
/// - <c>/cliente</c>: quello che vede il cliente, solo sui dispositivi autorizzati (agli altri un errore e basta);
/// - <c>/auth</c>: un dispositivo nuovo mostra il codice da scrivere nel back office; autorizzato, va su /cliente;
/// - <c>/backoffice</c>: con la password, configurazione, stato, statistiche e log.
/// Poi <c>/ws</c> (i messaggi del protocollo, via WebSocket) e <c>/diagnostica</c> (una cassa dal vivo, dal back office).
/// </summary>
public static class AgentApp
{
    public const string ServiceName = "BluetechDisplayAgent";

    /// <summary>Chiesto dal back office: Program rilancia l'applicazione (da console; da servizio la riavvia Windows).</summary>
    public static volatile bool RestartRequested;

    static readonly Dictionary<string, string> Switches = new()
    {
        ["--porta"] = "Agent:Porta",
        ["--dati"] = "Agent:CartellaDati",
        ["--replay"] = "Agent:Replay",
        ["--velocita"] = "Agent:VelocitaReplay",
        ["--ripeti"] = "Agent:RipetiReplay",
    };

    static readonly Dictionary<string, byte[]> Resources = [];

    /// <summary>Un file incorporato nell'exe (pagine, PWA); null se non c'è.</summary>
    internal static byte[]? Resource(string name)
    {
        lock (Resources)
        {
            if (!Resources.TryGetValue(name, out var bytes))
            {
                using var stream = typeof(AgentApp).Assembly.GetManifestResourceStream(name);
                if (stream == null)
                    return null;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                Resources[name] = bytes = memory.ToArray();
            }
            return bytes;
        }
    }

    internal static string Page(string name) => new System.Text.UTF8Encoding(false).GetString(Resource(name)!).TrimStart('\uFEFF');

    /// <summary>
    /// Quello che serve per installare /cliente come app: il manifest (schermo intero, si parte da /auth: su iPhone l'app
    /// installata non ha i cookie di Safari e si riabbina), le icone, il colore e lo script dell'invito.
    /// </summary>
    const string PwaHead = """
        <link rel="manifest" href="/manifest.webmanifest">
        <meta name="theme-color" content="#0b1017">
        <meta name="mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-capable" content="yes">
        <meta name="apple-mobile-web-app-status-bar-style" content="black">
        <meta name="apple-mobile-web-app-title" content="Monitor">
        <link rel="apple-touch-icon" href="/pwa/icona-180.png">
        <script src="/pwa/pwa.js" defer></script>

        """;

    internal static string WithPwa(string html)
    {
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 ? PwaHead + html : html.Insert(head, PwaHead);
    }

    static IResult Html(string name) => Results.Text(Page(name), "text/html; charset=utf-8");

    /// <summary>
    /// Le pagine del dispositivo (abbinamento, non abilitato, mi ricollego) sul tema del primo marchio, con il suo nome:
    /// il monitor conosce già il suo aspetto prima di essere abbinato (SPEC §21). Solo colori, font e nome: niente che
    /// il cliente non veda già sullo schermo.
    /// </summary>
    internal static string DevicePage(string name, AgentSettings settings)
    {
        var page = Page(name);
        if (settings.Marchi.FirstOrDefault() is not { } brand)
            return page.Replace("{{locale}}", "");
        var look = brand.Aspetto;
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var daytime = AgentSettings.ParseTime(look.GiornoDalle) is { } from && AgentSettings.ParseTime(look.GiornoAlle) is { } to && now >= from && now < to;
        var palette = Palettes.Resolve(look.Tema, look.Modo, daytime);
        if (AgentSettings.LocaleColors(brand) is { } colors)
            palette = ThemeColors.WithLocaleColors(palette, colors);
        string Font(string? family, string fallback) => family != null && Palettes.Fonts.TryGetValue(family, out var stack) ? stack : fallback;
        var css = $":root {{ color-scheme: {(palette.Scuro ? "dark" : "light")}; --bg: {palette.Bg}; --surface: {palette.SurfaceSolid}; --line: {palette.Line}; " +
            $"--text: {palette.Text}; --muted: {palette.Muted}; --faint: {palette.Faint}; --primary: {palette.Primary}; --glow: {palette.Glow}; " +
            $"--font-display: {Font(look.FontTitoli, palette.FontDisplay)}; --font-text: {Font(look.FontTesto, palette.FontText)}; }}";
        var venue = brand.Identita.NomeLocale.Trim() is { Length: > 0 } shown ? shown : brand.Nome;
        return page.Replace("/*TEMA*/", css).Replace("{{locale}}", System.Net.WebUtility.HtmlEncode(venue));
    }

    /// <param name="configure">Per i test: altra configurazione prima di costruire l'applicazione.</param>
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,   // da servizio la cartella corrente è System32
        });
        builder.Configuration.AddCommandLine(args, Switches);
        builder.Host.UseWindowsService(o => o.ServiceName = ServiceName);
        configure?.Invoke(builder);

        var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
        var dataFolder = DataFolder(options);
        var logFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CartellaLog));
        var store = new SettingsStore(dataFolder);
        var passwordFiles = PasswordFile.Apply(dataFolder, store);   // dall'installazione o dal tecnico, prima di tutto
        var network = store.Current.Installazione.Rete;
        if (!network.ApertoInRete && string.IsNullOrEmpty(builder.Configuration["AllowedHosts"]))
            builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";   // niente DNS rebinding sul back office
        builder.Logging.AddProvider(new FileLoggerProvider(logFolder, options.GiorniLog));

        // Una porta occupata (da un altro programma, o cambiata a mano) non deve tenere spento l'Agent: si ripiega sulla
        // 5080, o si parte senza HTTPS, e lo si dice nel back office.
        var startupProblems = new List<string>();
        var port = options.Porta ?? network.Porta;
        if (port != 0 && !PortAvailable(port, network.ApertoInRete))
        {
            if (port != 5080 && PortAvailable(5080, network.ApertoInRete))
            {
                startupProblems.Add($"La porta {port} è occupata da un altro programma: l'Agent ha usato la 5080. Libera la porta o cambiala in Opzioni.");
                port = 5080;
            }
            else
            {
                startupProblems.Add($"La porta {port} è occupata da un altro programma.");
            }
        }
        HttpsCertificate.Loaded? https = null;
        string? httpsProblem = null;
        if (network.Https.Attivo && network.Https.Porta != 0 && (network.Https.Porta == port || !PortAvailable(network.Https.Porta, network.ApertoInRete)))
            httpsProblem = $"HTTPS non attivo: la porta {network.Https.Porta} è occupata. Liberala o cambiala in Opzioni.";
        else if (network.Https.Attivo)
            https = HttpsCertificate.Load(network.Https, dataFolder, out httpsProblem);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            Listen(kestrel, network.ApertoInRete, port, null);
            if (https != null)
                Listen(kestrel, network.ApertoInRete, network.Https.Porta, https.Certificate);
        });

        var runtime = new AgentRuntime
        {
            DataFolder = dataFolder,
            LogFolder = logFolder,
            DisplayFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CartellaDisplay)),
            StartupNetwork = JsonSerializer.Deserialize<NetworkSettings>(JsonSerializer.Serialize(network, AgentSettings.Json), AgentSettings.Json)!,
            PortOverride = options.Porta,
            Certificate = https?.Certificate,
            AuthorityPath = https?.AuthorityPath,
            HttpsProblem = httpsProblem,
            StartupProblems = startupProblems,
        };
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(new Statistics(dataFolder));
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton<AdminAccess>();
        builder.Services.AddSingleton<DeviceAccess>();
        builder.Services.AddSingleton<DisplayHub>();
        builder.Services.AddSingleton<MediaLibrary>();
        builder.Services.AddSingleton<AgentEngine>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentEngine>());

        var app = builder.Build();
        var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Avvio");
        foreach (var note in store.LoadNotes)
            startup.LogWarning("{Note}", note);
        if (app.Services.GetRequiredService<Statistics>().LoadProblem is { } statsProblem)
            startup.LogWarning("{Problem}", statsProblem);
        foreach (var note in app.Services.GetRequiredService<MediaLibrary>().Tidy())
            startup.LogWarning("Immagini e video: {Note}", note);
        foreach (var passwordFile in passwordFiles)
        {
            if (passwordFile.Problem)
                startup.LogError("{Note}", passwordFile.Note);
            else
                startup.LogWarning("{Note}", passwordFile.Note);   // un cambio di password si deve vedere tra gli avvisi
        }
        if (httpsProblem != null)
            startup.LogError("{Problem}", httpsProblem);
        foreach (var problem in startupProblems)
            startup.LogError("{Problem}", problem);
        if (options.Porta.HasValue && options.Porta != network.Porta)
            startup.LogInformation("Porta {Port} dalla riga di comando, al posto della {Configured} delle impostazioni", options.Porta, network.Porta);

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        MapPages(app, options, runtime);
        BackOfficeApi.Map(app);
        return app;
    }

    static string DataFolder(AgentOptions options) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CartellaDati));

    /// <summary>La cartella dati come la vedrà l'Agent (appsettings.json e --dati), per i comandi da riga di comando.</summary>
    public static string DataFolder(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddCommandLine(args, Switches)
            .Build();
        return DataFolder(configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions());
    }

    /// <summary>
    /// La porta è libera come la vorrà Kestrel: su tutte le interfacce (IPv4 e IPv6) o solo su localhost. Un indirizzo
    /// alla volta: su Windows un socket IPv6 "dual mode" si lega anche se un altro programma ascolta solo in IPv4.
    /// </summary>
    internal static bool PortAvailable(int port, bool open)
    {
        foreach (var address in open ? new[] { IPAddress.Any, IPAddress.IPv6Any } : [IPAddress.Loopback, IPAddress.IPv6Loopback])
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
                continue;
            try
            {
                using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                if (address.Equals(IPAddress.IPv6Any))
                    socket.DualMode = true;   // come Kestrel
                socket.Bind(new IPEndPoint(address, port));
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                return false;
            }
            catch (SocketException)
            {
                // IPv6 non disponibile e simili: decide Kestrel
            }
        }
        return true;
    }

    static void Listen(KestrelServerOptions kestrel, bool open, int port, X509Certificate2? certificate)
    {
        Action<ListenOptions> secure = listen =>
        {
            if (certificate != null)
                listen.UseHttps(certificate);
        };
        if (port == 0)
            kestrel.Listen(IPAddress.Loopback, 0, secure);   // prove: una porta libera
        else if (open)
            kestrel.ListenAnyIP(port, secure);
        else
            kestrel.ListenLocalhost(port, secure);
    }

    static void MapPages(WebApplication app, AgentOptions options, AgentRuntime runtime)
    {
        // Le API che cambiano qualcosa accettano solo le pagine dell'Agent: un sito qualunque aperto nel browser della
        // cassa non può autorizzare dispositivi né toccare le impostazioni.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
                && !OriginAllowed(context, options.AltreOrigini))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });

        app.MapGet("/", () => Results.Redirect("/cliente"));
        app.MapGet("/display", () => Results.Redirect("/cliente"));   // l'indirizzo predefinito del Player di prima
        app.MapGet("/display/{**rest}", () => Results.Redirect("/cliente"));

        app.MapGet("/cliente", (HttpContext context, DeviceAccess devices, AdminAccess admin, Statistics stats) =>
        {
            // ?anteprima=cassa1 (dal vivo) o ?anteprima=demo (un conto finto): l'anteprima del back office, solo con la sua
            // sessione. Sempre la pagina dell'Agent, che riceve anche la bozza dell'aspetto dal back office.
            var preview = context.Request.Query.ContainsKey("anteprima");
            var device = devices.Find(context);
            if (preview && !admin.IsAuthenticated(context))
                return device != null ? Results.Redirect("/cliente") : Denied(context);
            if (!preview)
            {
                if (device == null)
                    return Denied(context);
                devices.Refresh(context);
                stats.Seen(device.Id, Remote(context), connected: false);
            }
            NoCache(context);
            var display = Path.Combine(runtime.DisplayFolder, "index.html");
            var page = !preview && File.Exists(display) ? File.ReadAllText(display) : Page("cliente.html");   // il display del design, quando c'è (M5)
            return Results.Content(preview ? page : WithPwa(page), "text/html; charset=utf-8");
        });

        // L'aspetto: le palette del design (pubbliche), i font del monitor, le immagini e i video caricati dal back office
        // (solo per chi può vedere lo schermo del cliente).
        app.MapGet("/aspetto/tema.js", (HttpContext context) =>
        {
            NoCache(context);
            return Results.Text(ThemeScript.Value, "text/javascript; charset=utf-8");
        });
        app.MapGet("/fonts/{name}", (string name, HttpContext context) =>
        {
            var type = Path.GetExtension(name) switch
            {
                ".woff2" => "font/woff2",
                ".css" => "text/css; charset=utf-8",
                _ => null,
            };
            if (type == null || name.Contains("..", StringComparison.Ordinal))
                return Results.NotFound();
            var own = Path.Combine(runtime.DisplayFolder, "fonts", name);   // quelli del display del design, se c'è
            if (File.Exists(own))
                return Results.File(own, type);
            if (Resource("fonts/" + name) is not { } bytes)
                return Results.NotFound();
            context.Response.Headers.CacheControl = type == "font/woff2" ? "public, max-age=31536000, immutable" : "no-cache";
            return Results.Bytes(bytes, type);
        });
        app.MapGet("/media/{id}", (string id, HttpContext context, DeviceAccess devices, AdminAccess admin, MediaLibrary media) =>
        {
            if ((devices.Find(context) == null && !admin.IsAuthenticated(context)) || media.PathOf(id) is not { } path)
                return Results.NotFound();
            var type = Path.GetExtension(id) switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".mp4" => "video/mp4",
                _ => "video/webm",
            };
            // Il nome è l'impronta del contenuto: lo stesso nome è sempre lo stesso file.
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(path, type, enableRangeProcessing: true);
        });

        // La PWA: pubblica, senza dati (il browser chiede manifest e icone senza cookie).
        app.MapGet("/manifest.webmanifest", (HttpContext context) =>
        {
            NoCache(context);
            return Results.Text(Page("pwa/manifest.webmanifest"), "application/manifest+json; charset=utf-8");
        });
        app.MapGet("/sw.js", (HttpContext context) =>
        {
            NoCache(context);   // un service worker nuovo deve arrivare subito
            return Results.Text(Page("pwa/sw.js"), "text/javascript; charset=utf-8");
        });
        app.MapGet("/pwa/{name}", (string name, HttpContext context) =>
        {
            var type = Path.GetExtension(name) switch
            {
                ".js" => "text/javascript; charset=utf-8",
                ".html" => "text/html; charset=utf-8",
                ".png" => "image/png",
                _ => null,
            };
            if (type == null || name == "manifest.webmanifest" || name == "sw.js" || Resource("pwa/" + name) is not { } bytes)
                return Results.NotFound();
            context.Response.Headers.CacheControl = type == "image/png" ? "public, max-age=86400" : "no-cache";
            if (name == "offline.html")
                return Results.Text(DevicePage("pwa/offline.html", context.RequestServices.GetRequiredService<SettingsStore>().Current), type);
            return Results.Bytes(bytes, type);
        });
        app.MapGet("/auth", (HttpContext context, DeviceAccess devices, SettingsStore settings) =>
        {
            if (devices.Find(context) != null)
            {
                devices.Refresh(context);
                return Results.Redirect("/cliente");
            }
            NoCache(context);
            return Results.Text(DevicePage("auth.html", settings.Current), "text/html; charset=utf-8");
        });
        app.MapPost("/api/abbinamento", (HttpContext context, DeviceAccess devices) =>
        {
            // Già autorizzato, anche se la pagina è stata ricaricata prima di accorgersene: niente codice nuovo.
            if (devices.Find(context) != null || devices.Poll(context) == "autorizzato")
                return Results.Json(new { stato = "autorizzato" });
            var (code, expiresIn) = devices.StartPairing(context);
            return Results.Json(new { stato = "attesa", codice = code, scadeTraSecondi = (int)expiresIn.TotalSeconds });
        });
        app.MapGet("/api/abbinamento", (HttpContext context, DeviceAccess devices) =>
        {
            NoCache(context);
            if (devices.Find(context) == null)
                return Results.Json(new { stato = devices.Poll(context) });
            devices.Refresh(context);   // la pagina aperta per mesi lo chiede ogni tanto: il cookie non scade
            return Results.Json(new { stato = "autorizzato" });
        });

        // Modalità demo: dove porta il QR finto dello scontrino digitale. La copia di cortesia com'è, per vedere sul
        // telefono che il giro funziona; gli ultimi 20 scontrini, poi "scaduto" come farebbe il servizio vero.
        app.MapGet("/prova/scontrino/{id}", (string id, HttpContext context, AgentEngine engine) =>
        {
            NoCache(context);
            var receipt = engine.DemoReceipt(id);
            var text = receipt == null
                ? "Questo scontrino di prova non c'è più: l'Agent tiene solo gli ultimi 20."
                : Stampa.PaperCopy.ToText(Stampa.PaperCopy.Compose(receipt, new PaperCopyOptions { Dicitura = "" }), 42);
            var page = Page("prova-scontrino.html").Replace("{{testo}}", System.Net.WebUtility.HtmlEncode(text.TrimEnd()));
            return Results.Content(page, "text/html; charset=utf-8", statusCode: receipt == null ? 404 : 200);
        });

        app.MapGet("/backoffice", (HttpContext context) =>
        {
            NoCache(context);
            return Html("backoffice.html");
        });
        app.MapGet("/diagnostica", (HttpContext context, AdminAccess admin) =>
        {
            if (!admin.IsAuthenticated(context))
                return Results.Redirect("/backoffice");
            NoCache(context);
            return Html("diagnostica.html");
        });

        app.Map("/ws", async (HttpContext context, AgentEngine engine, DisplayHub hub, DeviceAccess devices, AdminAccess admin, Statistics stats, IHostApplicationLifetime lifetime) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Qui serve un collegamento WebSocket.");
                return;
            }
            if (!OriginAllowed(context, options.AltreOrigini))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            // L'anteprima del back office (?cassa=…) vale anche su un dispositivo autorizzato; altrimenti conta il dispositivo.
            var preview = context.Request.Query["cassa"].ToString();
            string? deviceId = null;
            if (preview.Length == 0 || !admin.IsAuthenticated(context))
            {
                deviceId = devices.Find(context)?.Id;
                if (deviceId == null && !admin.IsAuthenticated(context))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var remote = Remote(context);
            var client = engine.Connect(socket, remote, deviceId, preview.Length > 0 ? preview : null);
            if (client == null)
            {
                await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation, "cassa non disponibile", CancellationToken.None);
                return;
            }
            if (deviceId != null)
                stats.Seen(deviceId, remote, connected: true);
            // Anche l'arresto del servizio chiude il collegamento: altrimenti ogni stop aspetterebbe 30 s.
            using var end = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
            await hub.RunAsync(client, end.Token, engine.OnDisplayMessage);
        });

        // I file del display del design (M5): script, stili, immagini. La sua pagina passa solo da /cliente.
        var displayFolder = runtime.DisplayFolder;
        try
        {
            Directory.CreateDirectory(displayFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (Directory.Exists(displayFolder))
        {
            app.Use(async (context, next) =>
            {
                if (context.GetEndpoint() == null && context.Request.Path.Value?.EndsWith(".html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await next();
            });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(displayFolder), OnPrepareResponse = CacheHeaders });
        }
    }

    /// <summary>tema.js con le palette e i font dell'Agent: un'unica fonte per il C# e per le pagine.</summary>
    static readonly Lazy<string> ThemeScript = new(() =>
    {
        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return Page("tema.js")
            .Replace("/*PALETTES*/{}", JsonSerializer.Serialize(Palettes.All, camel))
            .Replace("/*FONTS*/{}", JsonSerializer.Serialize(Palettes.Fonts));
    });

    static IResult Denied(HttpContext context) => Results.Content(DevicePage("negato.html", context.RequestServices.GetRequiredService<SettingsStore>().Current),
        "text/html; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);

    static void NoCache(HttpContext context) => context.Response.Headers.CacheControl = "no-cache";

    internal static string Remote(HttpContext context) => context.Connection.RemoteIpAddress is { } ip
        ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
        : "?";

    /// <summary>I file di assets/ hanno l'impronta nel nome e non cambiano mai.</summary>
    static void CacheHeaders(StaticFileResponseContext context)
    {
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        else
            context.Context.Response.Headers.CacheControl = "no-cache";
    }

    /// <summary>
    /// Un browser manda sempre l'origine della pagina: si accettano le pagine servite dall'Agent stesso (Player,
    /// display, back office) e quelle in <see cref="AgentOptions.AltreOrigini"/>. Senza origine non è un browser.
    /// </summary>
    internal static bool OriginAllowed(HttpContext context, IEnumerable<string> extra)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length == 0)
            return true;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
            return true;
        return extra.Any(o => string.Equals(o.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}
