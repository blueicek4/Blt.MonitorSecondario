using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Stampa;
using Bluetech.Display.Agent.Vendita;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Le API del back office, sotto /api/bo: l'accesso con la password, poi casse, stampanti, dispositivi, opzioni,
/// stato, statistiche e log. Gli oggetti hanno gli stessi nomi di impostazioni.json.
/// </summary>
public static class BackOfficeApi
{
    const int MaxBody = 64 * 1024;
    static readonly JsonNodeOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    static readonly Lazy<string> TestQr = new(() =>
    {
        using var stream = typeof(BackOfficeApi).Assembly.GetManifestResourceStream("qr-prova.txt")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ReceiptParser.Parse(memory.ToArray()).QrDataUri!;
    });

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/bo/sessione", (HttpContext context, AdminAccess admin) => Json(new JsonObject
        {
            ["autenticato"] = admin.IsAuthenticated(context),
            ["passwordImpostata"] = admin.PasswordSet,
            ["locale"] = AdminAccess.IsLocal(context),
            ["raggiungibile"] = admin.Reachable(context),
        }));

        app.MapPost("/api/bo/accesso", async (HttpContext context, AdminAccess admin, ILogger<AdminAccess> log) =>
        {
            var body = await Body(context);
            var error = admin.Login(context, Text(body, "password"));
            if (error == null)
                return Ok();
            log.LogWarning("Back office: accesso rifiutato da {Remote} ({Error})", AgentApp.Remote(context), error);
            return Error(error, error == AdminAccess.OnlyFromThisPc ? 403 : error.StartsWith("Troppi", StringComparison.Ordinal) ? 429 : 401);
        });

        app.MapPost("/api/bo/esci", (HttpContext context, AdminAccess admin) =>
        {
            admin.EndSession(context);
            return Ok();
        });

        app.MapPost("/api/bo/password", async (HttpContext context, AdminAccess admin, SettingsStore settings, ILogger<AdminAccess> log) =>
        {
            var body = await Body(context);
            var next = Text(body, "nuova");
            if (AdminAccess.CheckNew(next) is { } weak)
                return Error(weak);
            var first = !admin.PasswordSet;
            if (first && !AdminAccess.IsLocal(context))
                return Error("La prima password si imposta dal PC dell'Agent: apri http://localhost/backoffice lì.", 403);
            if (!first)
            {
                if (!admin.IsAuthenticated(context))
                    return Error("Accedi prima al back office.", 401);
                if (!AdminAccess.Verify(settings.Current.Password!, Text(body, "attuale") ?? ""))
                    return Error("La password attuale non è giusta.");
            }
            var error = settings.Update(s =>
            {
                if (first && s.Password != null)
                    return "La password è già stata impostata.";
                s.Password = AdminAccess.Hash(next!);
                return null;
            });
            if (error != null)
                return Error(error);
            admin.EndAllSessions();
            admin.StartSession(context);
            if (first)
                log.LogInformation("Password del back office impostata");
            else
                log.LogInformation("Password del back office cambiata da {Remote}", AgentApp.Remote(context));
            return Ok();
        });

        var bo = app.MapGroup("/api/bo").AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (!context.RequestServices.GetRequiredService<AdminAccess>().IsAuthenticated(context))
                return Error("Accedi al back office.", 401);
            context.Response.Headers.CacheControl = "no-store";
            return await next(invocation);
        });

        // Lo stato completo, anche per l'assistenza (era /api/stato).
        app.MapGet("/api/stato", (HttpContext context, AdminAccess admin) =>
            admin.IsAuthenticated(context) ? Json(FullStatus(context)) : Error("Accedi al back office.", 401));
        bo.MapGet("/stato", (HttpContext context) => Json(FullStatus(context)));

        bo.MapGet("/impostazioni", (SettingsStore settings) => Json(ForUi(settings)));
        AppearanceApi.Map(bo);

        bo.MapPut("/opzioni", async (HttpContext context, SettingsStore settings, IServer server, ILogger<SettingsStore> log) =>
        {
            var body = await Body(context);
            if (body == null)
                return Error("Dati non validi.");
            var update = body.Deserialize<OptionsUpdate>(AgentSettings.Json);
            if (update?.Rete == null || update.ScontrinoDigitale == null)
                return Error("Dati non validi.");
            var network = update.Rete;
            network.Https ??= new HttpsSettings();
            if (network.Porta is < 1 or > 65535 || network.Https.Porta is < 1 or > 65535)
                return Error("Le porte vanno da 1 a 65535.");
            var certificate = (network.Https.Certificato ?? "").Trim();
            if (network.Https.Attivo && certificate.Length > 0
                && !File.Exists(Path.IsPathFullyQualified(certificate) ? certificate : Path.Combine(AppContext.BaseDirectory, certificate)))
                return Error($"Il certificato {certificate} non c'è.");
            // Una porta occupata da un altro programma terrebbe l'Agent fermo al prossimo avvio: meglio dirlo adesso.
            var bound = BoundPorts(server);
            foreach (var (port, name) in new[] { (network.Porta, "HTTP"), (network.Https.Attivo ? network.Https.Porta : 0, "HTTPS") })
            {
                if (port != 0 && !bound.Contains(port) && !AgentApp.PortAvailable(port, network.ApertoInRete))
                    return Error($"La porta {name} {port} è già usata da un altro programma su questo PC.");
            }
            var fromNetwork = update.BackOfficeDallaRete ?? settings.Current.BackOfficeDallaRete;
            var opened = fromNetwork != settings.Current.BackOfficeDallaRete;
            var error = settings.Update(s =>
            {
                s.BackOfficeDallaRete = fromNetwork;
                s.ChiusuraVenditaDopoSecondi = update.ChiusuraVenditaDopoSecondi;
                s.ScontrinoDigitale = update.ScontrinoDigitale;
                s.ScontrinoDigitale.Dicitura = (s.ScontrinoDigitale.Dicitura ?? "").Trim();
                var password = network.Https.PasswordCertificato ?? s.Rete.Https.PasswordCertificato;   // null = resta quella di prima
                s.Rete = network;
                s.Rete.Https.Certificato = certificate;
                s.Rete.Https.PasswordCertificato = password;
                return null;
            });
            if (error != null)
                return Error(error);
            log.LogInformation("Opzioni cambiate dal back office");
            if (opened && fromNetwork)
                log.LogWarning("Back office aperto anche alla rete da {Remote}", AgentApp.Remote(context));
            else if (opened)
                log.LogWarning("Back office di nuovo solo dal PC dell'Agent ({Remote})", AgentApp.Remote(context));
            return Ok();
        });

        bo.MapPost("/casse", async (HttpContext context, SettingsStore settings, Statistics stats, AgentRuntime runtime) =>
        {
            if (await Read<CashPointConfig>(context) is not { } cash)
                return Error("Dati non validi.");
            Tidy(cash);
            if (UnsafeCashFile(cash.FileCassa, runtime, settings.Current, null) is { } unsafeFile)
                return Error(unsafeFile);
            var used = stats.CashPointIds();
            var error = settings.Update(s =>
            {
                cash.Id = AgentSettings.NextId("cassa", s.Casse.Select(c => c.Id).Concat(used));   // un id nuovo non eredita i conteggi di una cassa tolta
                s.Casse.Add(cash);
                return null;
            });
            return error == null ? Ok(new JsonObject { ["id"] = cash.Id }) : Error(error);   // il motore scrive nel log la cassa nuova
        });
        bo.MapPut("/casse/{id}", async (string id, HttpContext context, SettingsStore settings, AgentRuntime runtime) =>
        {
            if (await Read<CashPointConfig>(context) is not { } changed)
                return Error("Dati non validi.");
            Tidy(changed);
            if (UnsafeCashFile(changed.FileCassa, runtime, settings.Current, id) is { } unsafeFile)
                return Error(unsafeFile);
            return Result(settings.Update(s =>
            {
                if (s.CashPoint(id) is not { } cash)
                    return "La cassa non c'è più.";
                cash.Nome = changed.Nome;
                cash.FileCassa = changed.FileCassa;
                return null;
            }));
        });
        bo.MapDelete("/casse/{id}", (string id, SettingsStore settings) => Result(settings.Update(s =>
        {
            if (s.CashPoint(id) is not { } cash)
                return null;
            var users = s.Dispositivi.Where(d => d.Cassa == id).Select(d => d.Nome).ToList();
            if (users.Count > 0)
                return $"{cash.Nome} è mostrata da {string.Join(", ", users)}: sposta prima i dispositivi su un'altra cassa.";
            if (s.Casse.Count == 1)
                return "Serve almeno una cassa.";
            s.Casse.Remove(cash);
            foreach (var set in s.Aspetto.Set)
            {
                if (set.Casse.Remove(id) && set.Casse.Count == 0)
                    set.Attivo = false;   // era solo per questa cassa: non deve passare a tutte
            }
            return null;
        })));

        bo.MapPost("/stampanti", async (HttpContext context, SettingsStore settings) =>
        {
            if (await Read<PrinterConfig>(context) is not { } printer)
                return Error("Dati non validi.");
            var error = settings.Update(s =>
            {
                printer.Id = AgentSettings.NextId("stampante", s.Stampanti.Select(p => p.Id));
                Tidy(printer);
                s.Stampanti.Add(printer);
                return null;
            });
            return error == null ? Ok(new JsonObject { ["id"] = printer.Id }) : Error(error);
        });
        bo.MapPut("/stampanti/{id}", async (string id, HttpContext context, SettingsStore settings) =>
        {
            if (await Read<PrinterConfig>(context) is not { } changed)
                return Error("Dati non validi.");
            return Result(settings.Update(s =>
            {
                var index = s.Stampanti.FindIndex(p => p.Id == id);
                if (index < 0)
                    return "La stampante non c'è più.";
                changed.Id = id;
                Tidy(changed);
                s.Stampanti[index] = changed;
                return null;
            }));
        });
        bo.MapDelete("/stampanti/{id}", (string id, SettingsStore settings) => Result(settings.Update(s =>
        {
            if (s.Printer(id) is not { } printer)
                return null;
            var users = s.Dispositivi.Where(d => d.Stampante == id).Select(d => d.Nome).ToList();
            if (users.Count > 0)
                return $"{printer.Nome} è usata da {string.Join(", ", users)}: togli prima la stampante dai dispositivi.";
            s.Stampanti.Remove(printer);
            return null;
        })));
        bo.MapPost("/stampanti/{id}/prova", async (string id, SettingsStore settings, AgentRuntime runtime, ILogger<AgentEngine> log) =>
        {
            var now = settings.Current;
            if (now.Printer(id) is not { } config)
                return Error("La stampante non c'è più.", 404);
            var printOptions = config.ToOptions(now.ScontrinoDigitale.Dicitura);
            if (CopyPrinters.Create(printOptions, runtime.LogFolder) is not { } printer)
                return Error("Manca l'indirizzo della stampante.");
            var blocks = PaperCopy.Compose(TestReceipt(config), printOptions);
            var text = PaperCopy.ToText(blocks, printOptions.Colonne);
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(printOptions.TimeoutSecondi, 1, 60)));
                await printer.PrintAsync(EscPos.Encode(blocks, printOptions), text, "PROVA", timeout.Token);
            }
            catch (Exception ex)
            {
                var problem = ex is OperationCanceledException ? "la stampante non risponde" : ex.Message;
                log.LogWarning("Stampa di prova su {Printer} non riuscita: {Problem}", printer.Description, problem);
                return Error($"Non stampata: {problem}.");
            }
            log.LogInformation("Stampa di prova inviata a {Printer} in {Ms} ms", printer.Description, watch.ElapsedMilliseconds);
            return Ok(new JsonObject { ["ms"] = watch.ElapsedMilliseconds, ["testo"] = text, ["stampante"] = printer.Description });
        });

        bo.MapPost("/dispositivi", async (HttpContext context, DeviceAccess devices, SettingsStore settings, ILogger<DeviceAccess> log) =>
        {
            var body = await Body(context);
            var error = devices.Approve(Text(body, "codice"), Text(body, "nome"), Text(body, "cassa"), Text(body, "stampante"), out var device);
            if (error != null)
                return Error(error);
            var now = settings.Current;
            log.LogInformation("Dispositivo {Name} autorizzato dal back office: mostra {CashPoint}, stampa su {Printer}",
                device!.Nome, now.CashPoint(device.Cassa)?.Nome ?? device.Cassa, now.Printer(device.Stampante)?.Nome ?? "nessuna stampante");
            return Ok(new JsonObject { ["id"] = device.Id });
        });
        bo.MapPut("/dispositivi/{id}", async (string id, HttpContext context, SettingsStore settings) =>
        {
            var body = await Body(context);
            if (body == null)
                return Error("Dati non validi.");
            return Result(settings.Update(s =>
            {
                if (s.Device(id) is not { } device)
                    return "Il dispositivo non c'è più.";
                device.Nome = (Text(body, "nome") ?? "").Trim();
                device.Cassa = Text(body, "cassa") ?? "";
                device.Stampante = Text(body, "stampante") is { Length: > 0 } printer ? printer : null;
                if (body.ContainsKey("disposizione"))
                    device.Disposizione = Text(body, "disposizione") is { Length: > 0 } layout ? layout : null;
                return null;
            }));
        });
        bo.MapDelete("/dispositivi/{id}", (string id, SettingsStore settings, Statistics stats, ILogger<DeviceAccess> log) =>
        {
            string? name = null;
            var error = settings.Update(s =>
            {
                name = s.Device(id)?.Nome;
                s.Dispositivi.RemoveAll(d => d.Id == id);
                return null;
            });
            if (error != null)
                return Error(error);
            stats.Forget(id);
            if (name != null)
                log.LogInformation("Dispositivo {Name} revocato dal back office", name);
            return Ok();
        });

        bo.MapGet("/statistiche", (int? giorni, Statistics stats, SettingsStore settings) => Json(new JsonObject
        {
            ["giorni"] = stats.Report(giorni ?? 31),
            ["casse"] = new JsonArray(settings.Current.Casse.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["nome"] = c.Nome }).ToArray()),
        }));

        bo.MapGet("/log", (string? giorno, int? righe, AgentRuntime runtime) =>
        {
            var days = Directory.Exists(runtime.LogFolder)
                ? Directory.GetFiles(runtime.LogFolder, "agent-*.log").Select(f => Path.GetFileNameWithoutExtension(f)["agent-".Length..]).OrderDescending().ToList()
                : [];
            var day = giorno is { Length: 8 } && giorno.All(char.IsAsciiDigit) ? giorno : days.FirstOrDefault() ?? DateTime.Now.ToString("yyyyMMdd");
            var lines = Tail(Path.Combine(runtime.LogFolder, $"agent-{day}.log"), Math.Clamp(righe ?? 400, 10, 5000));
            return Json(new JsonObject
            {
                ["giorni"] = new JsonArray(days.Select(d => (JsonNode)JsonValue.Create(d)!).ToArray()),
                ["giorno"] = day,
                ["righe"] = new JsonArray(lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
            });
        });

        bo.MapGet("/https/autorita", (AgentRuntime runtime) =>
            runtime.AuthorityPath is { } path && File.Exists(path)
                ? Results.File(path, "application/x-x509-ca-cert", HttpsCertificate.AuthorityFile)
                : Error("Nessuna autorità: c'è solo con HTTPS attivo e il certificato generato dall'Agent.", 404));

        bo.MapPost("/riavvia", (IHostApplicationLifetime lifetime, Statistics stats, ILogger<AgentEngine> log) =>
        {
            var service = WindowsServiceHelpers.IsWindowsService();
            log.LogWarning("Riavvio chiesto dal back office");
            stats.SaveIfDirty();
            _ = Task.Run(async () =>
            {
                await Task.Delay(500);   // prima parte la risposta
                if (service)
                    Environment.Exit(1);   // Windows lo fa ripartire dopo 5 s: le azioni di ripristino del servizio
                AgentApp.RestartRequested = true;
                lifetime.StopApplication();
            });
            return Ok(new JsonObject { ["servizio"] = service });
        });
    }

    sealed class OptionsUpdate
    {
        /// <summary>Null = com'era.</summary>
        public bool? BackOfficeDallaRete { get; set; }
        public int ChiusuraVenditaDopoSecondi { get; set; }
        public DigitalReceiptOptions? ScontrinoDigitale { get; set; }
        public NetworkSettings? Rete { get; set; }
    }

    /// <summary>
    /// L'Agent cancella il file della cassa dopo averlo letto: non deve poter essere un suo file (impostazioni, log,
    /// display) né un file che c'è già e non sembra quello del monitor (un percorso sbagliato lo cancellerebbe).
    /// </summary>
    static string? UnsafeCashFile(string file, AgentRuntime runtime, AgentSettings current, string? cashId)
    {
        if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file))
            return null;   // lo dice la validazione
        var full = Path.GetFullPath(file);
        if (current.CashPoint(cashId)?.FileCassa is { } unchanged && string.Equals(Path.GetFullPath(unchanged), full, StringComparison.OrdinalIgnoreCase))
            return null;   // il file di prima: è già sorvegliato
        foreach (var folder in new[] { runtime.DataFolder, runtime.LogFolder, runtime.DisplayFolder })
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return $"Il file della cassa non può stare nelle cartelle dell'Agent ({root.TrimEnd(Path.DirectorySeparatorChar)}): l'Agent lo cancella dopo averlo letto.";
        }
        try
        {
            if (!File.Exists(full))
                return null;
            var length = new FileInfo(full).Length;
            if (length > CashFileSource.MaxFileBytes
                || (length > 0 && ReceiptParser.Parse(File.ReadAllBytes(full)).Kind == CashFileKind.Message))
                return $"C'è già un file {full} che non sembra quello del monitor di Passepartout: l'Agent lo cancellerebbe. Controlla il percorso.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Non riesco a leggere {full}: {ex.Message}";
        }
        return null;
    }

    static void Tidy(CashPointConfig cash)
    {
        cash.Nome = (cash.Nome ?? "").Trim();
        cash.FileCassa = (cash.FileCassa ?? "").Trim().Trim('"');
    }

    static void Tidy(PrinterConfig printer)
    {
        printer.Nome = (printer.Nome ?? "").Trim();
        printer.Collegamento = (printer.Collegamento ?? "").Trim().ToLowerInvariant();
        printer.Indirizzo = (printer.Indirizzo ?? "").Trim();
        printer.TabellaCaratteri = (printer.TabellaCaratteri ?? "").Trim().ToLowerInvariant();
        printer.TestoRitiro = (printer.TestoRitiro ?? "").Trim();
    }

    /// <summary>Uno scontrino finto con accenti, euro, sconto, resto e un QR di prova (porta a example.com).</summary>
    static DigitalReceipt TestReceipt(PrinterConfig printer) => new()
    {
        Id = "PROVA",
        Png = TestQr.Value,
        Caption = "Stampa di prova",
        Bill = new CashFile
        {
            Kind = CashFileKind.Receipt,
            Hash = "prova",
            IsBill = true,
            Header = ["STAMPA DI PROVA", printer.Nome, "Bluetech Display Agent"],
            Entries =
            [
                new ItemEntry("Caffè", 2, 240, 120),
                new AdjustmentEntry("SCONTO", -40, true),
                new ItemEntry("Accenti: àèéìòù €", 1, 0, null),
            ],
            Total = 200,
            Payment = new Payment("CONTANTI", 500, 300),
        },
        IssuedAt = DateTimeOffset.Now,
        PaperCopy = true,
    };

    static JsonObject FullStatus(HttpContext context)
    {
        var services = context.RequestServices;
        var engine = services.GetRequiredService<AgentEngine>();
        var settings = services.GetRequiredService<SettingsStore>();
        var runtime = services.GetRequiredService<AgentRuntime>();
        var devices = services.GetRequiredService<DeviceAccess>();
        var stats = services.GetRequiredService<Statistics>();
        var server = services.GetRequiredService<IServer>();
        var now = settings.Current;

        var status = engine.Status();
        status["servizio"] = WindowsServiceHelpers.IsWindowsService();
        status["riavvioNecessario"] = runtime.RestartNeeded(now);
        status["cartellaDati"] = runtime.DataFolder;
        status["indirizzi"] = Addresses(server, runtime.StartupNetwork.ApertoInRete);

        var notes = new JsonArray();
        foreach (var note in settings.LoadNotes.Where(n => !n.StartsWith("Primo avvio", StringComparison.Ordinal)))
            notes.Add(note);
        if (runtime.HttpsProblem != null)
            notes.Add(runtime.HttpsProblem);
        foreach (var problem in runtime.StartupProblems)
            notes.Add(problem);
        if (stats.LoadProblem != null)
            notes.Add(stats.LoadProblem);
        foreach (var old in engine.OldMonitors)
            notes.Add($"Il vecchio MonitorCliente.exe è aperto ({old}): va chiuso e tolto dall'avvio automatico, l'Agent toglie il file che legge.");
        status["avvisi"] = notes;

        status["https"] = runtime.Certificate is { } certificate
            ? new JsonObject
            {
                ["soggetto"] = certificate.Subject,
                ["scade"] = certificate.NotAfter.ToString("O"),
                ["generato"] = runtime.AuthorityPath != null,
            }
            : null;

        var online = engine.ConnectedDevices();
        status["dispositivi"] = new JsonArray(now.Dispositivi.Select(d =>
        {
            var seen = stats.LastSeen(d.Id);
            return (JsonNode)new JsonObject
            {
                ["id"] = d.Id,
                ["nome"] = d.Nome,
                ["cassa"] = d.Cassa,
                ["stampante"] = d.Stampante,
                ["collegato"] = online.Contains(d.Id),
                ["ultimoAccesso"] = seen?.UltimoAccesso.ToString("O"),
                ["indirizzo"] = seen?.Indirizzo,
                ["copieCartacee"] = seen?.CopieCartacee ?? 0,
                ["autorizzatoIl"] = d.AutorizzatoIl.ToString("O"),
            };
        }).ToArray());
        status["inAttesa"] = new JsonArray(devices.Waiting().Select(w => (JsonNode)new JsonObject
        {
            ["indirizzo"] = w.Remote,
            ["secondi"] = (int)w.Age.TotalSeconds,
        }).ToArray());
        return status;
    }

    static HashSet<int> BoundPorts(IServer server) =>
        (server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(a => Uri.TryCreate(a.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"), UriKind.Absolute, out var uri) ? uri.Port : -1)
            .ToHashSet();

    /// <summary>Gli indirizzi da aprire sui dispositivi: con l'Agent aperto in rete, quelli del PC; altrimenti localhost.</summary>
    static JsonArray Addresses(IServer server, bool open)
    {
        var result = new JsonArray();
        var bound = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var hosts = open
            ? HttpsCertificate.CurrentNames().Ips.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)).Select(ip => ip.ToString()).ToList()
            : [];
        if (hosts.Count == 0)
            hosts.Add("localhost");
        foreach (var address in bound)
        {
            if (!Uri.TryCreate(address.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"), UriKind.Absolute, out var uri))
                continue;
            foreach (var host in hosts)
            {
                var url = $"{uri.Scheme}://{host}:{uri.Port}";
                if (!result.Any(n => (string?)n == url))
                    result.Add(url);
            }
        }
        return result;
    }

    static JsonObject ForUi(SettingsStore store)
    {
        var settings = store.Current;
        var json = JsonSerializer.SerializeToNode(settings, AgentSettings.Json)!.AsObject();
        json.Remove("Password");
        json["PasswordImpostata"] = settings.Password != null;
        var https = json["Rete"]!["Https"]!.AsObject();
        https["PasswordCertificatoImpostata"] = settings.Rete.Https.PasswordCertificato.Length > 0;
        https.Remove("PasswordCertificato");
        foreach (var device in json["Dispositivi"]!.AsArray())
            device!.AsObject().Remove("Token");
        json["File"] = store.FilePath;
        return json;
    }

    /// <summary>Le ultime righe del log del giorno (al massimo gli ultimi 2 MB), con il file aperto dall'Agent.</summary>
    static List<string> Tail(string path, int count)
    {
        if (!File.Exists(path))
            return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const long window = 2 * 1024 * 1024;
        var skipFirst = stream.Length > window;
        if (skipFirst)
            stream.Seek(-window, SeekOrigin.End);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var lines = new List<string>();
        if (skipFirst)
            reader.ReadLine();   // a metà riga
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.Count > count ? lines[^count..] : lines;
    }

    internal static async Task<JsonObject?> Body(HttpContext context)
    {
        if (context.Request.ContentLength > MaxBody)
            return null;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var buffer = new char[MaxBody + 1];
        var length = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (length > MaxBody)
            return null;
        try
        {
            return JsonNode.Parse(new string(buffer, 0, length), CaseInsensitive) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static async Task<T?> Read<T>(HttpContext context) where T : class
    {
        var body = await Body(context);
        try
        {
            return body?.Deserialize<T>(AgentSettings.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Text(JsonObject? body, string name) =>
        body?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static IResult Json(JsonNode node) => Results.Text(node.ToJsonString(AgentSettings.Json), "application/json; charset=utf-8");

    internal static IResult Ok(JsonObject? extra = null)
    {
        var json = extra ?? new JsonObject();
        json["ok"] = true;
        return Json(json);
    }

    internal static IResult Error(string message, int status = StatusCodes.Status400BadRequest) =>
        Results.Text(new JsonObject { ["errore"] = message }.ToJsonString(AgentSettings.Json), "application/json; charset=utf-8", statusCode: status);

    internal static IResult Result(string? error) => error == null ? Ok() : Error(error);
}
