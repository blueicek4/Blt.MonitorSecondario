using System.Net;
using System.Text.Json;
using Bluetech.Display.Agent.Logging;
using Bluetech.Display.Agent.Server;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Bluetech.Display.Agent;

/// <summary>
/// L'applicazione: il servizio che legge la cassa e il server per i display.
/// - <c>/</c>: il display (la build di display/ nella cartella <see cref="AgentOptions.CartellaDisplay"/>);
///   <c>/display</c> rimanda lì, perché è l'indirizzo predefinito del Player;
/// - <c>/ws</c>: i messaggi del protocollo, via WebSocket;
/// - <c>/diagnostica</c>: cosa arriva dalla cassa, dal vivo; <c>/api/stato</c>: lo stesso in JSON;
/// - <c>/cliente</c>: la stessa pagina senza le parti tecniche, per un palmare o uno schermo touch finché il display
///   vero non c'è (scontrino digitale e copia cartacea, docs/03).
/// </summary>
public static class AgentApp
{
    public const string ServiceName = "BluetechDisplayAgent";

    static readonly Dictionary<string, string> Switches = new()
    {
        ["--file"] = "Agent:FileCassa",
        ["--porta"] = "Agent:Porta",
        ["--replay"] = "Agent:Replay",
        ["--velocita"] = "Agent:VelocitaReplay",
        ["--ripeti"] = "Agent:RipetiReplay",
    };

    static readonly Lazy<string> DiagnosticPage = new(() =>
    {
        using var stream = typeof(AgentApp).Assembly.GetManifestResourceStream("diagnostica.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
        if (!options.ApertoInRete && string.IsNullOrEmpty(builder.Configuration["AllowedHosts"]))
            builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";   // niente DNS rebinding su /api/stato
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, options.CartellaLog), options.GiorniLog));
        builder.WebHost.UseUrls(options.Porta == 0
            ? "http://127.0.0.1:0"
            : options.ApertoInRete ? $"http://*:{options.Porta}" : $"http://localhost:{options.Porta}");

        builder.Services.AddSingleton<DisplayHub>();
        builder.Services.AddSingleton<AgentEngine>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AgentEngine>());

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        if (options.ChiaviDispositivi.Length > 0)
        {
            app.Use(async (context, next) =>
            {
                if (DeviceAllowed(context, options.ChiaviDispositivi))
                {
                    await next();
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Dispositivo non abbinato: apri una volta l'indirizzo con ?chiave=… (la chiave è nell'appsettings.json dell'Agent).");
            });
        }

        app.Map("/ws", async (HttpContext context, AgentEngine engine, DisplayHub hub, IHostApplicationLifetime lifetime) =>
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
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var client = engine.Connect(socket, context.Connection.RemoteIpAddress?.ToString() ?? "?");
            // Anche l'arresto del servizio chiude il collegamento: altrimenti ogni stop aspetterebbe 30 s.
            using var end = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
            await hub.RunAsync(client, end.Token, engine.OnDisplayMessage);
        });
        app.MapGet("/api/stato", (AgentEngine engine) =>
            Results.Text(engine.Status().ToJsonString(Indented), "application/json; charset=utf-8"));
        app.MapGet("/diagnostica", () => Results.Text(DiagnosticPage.Value, "text/html; charset=utf-8"));
        app.MapGet("/cliente", () => Results.Text(DiagnosticPage.Value, "text/html; charset=utf-8"));
        app.MapGet("/display", () => Results.Redirect("/"));
        app.MapGet("/display/{**rest}", () => Results.Redirect("/"));

        var displayFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.CartellaDisplay));
        try
        {
            Directory.CreateDirectory(displayFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (Directory.Exists(displayFolder))
        {
            var files = new PhysicalFileProvider(displayFolder);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files, OnPrepareResponse = CacheHeaders });
        }
        // Finché nella cartella non c'è il display, "/" dice cosa manca. Si controlla a ogni richiesta: un display
        // copiato con l'Agent acceso si vede subito.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/" && !File.Exists(Path.Combine(displayFolder, "index.html")))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(Placeholder(displayFolder));
                return;
            }
            await next();
        });
        return app;
    }

    /// <summary>
    /// Le pagine si riconvalidano sempre: senza, WebView2 terrebbe per giorni il display vecchio dopo un aggiornamento.
    /// I file di assets/ hanno l'impronta nel nome e non cambiano mai.
    /// </summary>
    static void CacheHeaders(StaticFileResponseContext context)
    {
        if (context.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            context.Context.Response.Headers.CacheControl = "no-cache";
        else if (context.Context.Request.Path.StartsWithSegments("/assets"))
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }

    /// <summary>
    /// Con le chiavi dei dispositivi: questo PC entra sempre; un palmare o un tablet solo con <c>?chiave=…</c>, che poi
    /// resta in un cookie (così anche la pagina, i suoi file e il WebSocket passano).
    /// </summary>
    internal static bool DeviceAllowed(HttpContext context, IReadOnlyCollection<string> keys)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote == null || IPAddress.IsLoopback(remote) || (remote.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remote.MapToIPv4())))
            return true;
        var key = context.Request.Query["chiave"].ToString();
        if (key.Length > 0 && keys.Contains(key))
        {
            context.Response.Cookies.Append(DeviceCookie, key, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddYears(5),
            });
            return true;
        }
        return context.Request.Cookies.TryGetValue(DeviceCookie, out var cookie) && keys.Contains(cookie);
    }

    const string DeviceCookie = "bluetech_dispositivo";

    /// <summary>
    /// Un browser manda sempre l'origine della pagina: si accettano le pagine servite dall'Agent stesso (Player,
    /// diagnostica, display in rete) e quelle in <see cref="AgentOptions.AltreOrigini"/>. Senza origine non è un browser.
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

    /// <summary>
    /// Si ricarica ogni 10 s: il Player non riapre la pagina da solo, così il display copiato più tardi compare senza
    /// toccare il monitor cliente.
    /// </summary>
    static string Placeholder(string folder) => $$"""
        <!doctype html>
        <html lang="it"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Bluetech Display Agent</title><link rel="icon" href="data:,">
        <meta http-equiv="refresh" content="10">
        <style>body{font:16px/1.5 system-ui,sans-serif;background:#0b1017;color:#e6eaf2;margin:0;padding:48px}a{color:#8fb8ff}</style>
        </head><body>
        <h1>Bluetech Display Agent</h1>
        <p>L'Agent è in funzione, ma il display non è installato in <code>{{System.Net.WebUtility.HtmlEncode(folder)}}</code>.</p>
        <p>Cosa arriva dalla cassa: <a href="/diagnostica">diagnostica</a>.</p>
        </body></html>
        """;
}
