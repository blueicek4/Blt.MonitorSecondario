using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Le API del back office, sotto /api/bo: l'accesso con la password del titolare o del tecnico, poi quello che vede il
/// titolare (<see cref="BrandApi"/>: il locale, le vetrine, i contenuti, gli schermi; qui lo stato e le statistiche) e
/// quello che vede solo il tecnico (<see cref="InstallationApi"/>, e qui il log e il riavvio). Gli oggetti hanno gli
/// stessi nomi di impostazioni.json.
/// </summary>
public static class BackOfficeApi
{
    const int MaxBody = 64 * 1024;
    /// <summary>Un marchio intero, con 200 promozioni e 30 vetrine, sta abbondantemente in 2 MB.</summary>
    internal const int MaxBrandBody = 2 * 1024 * 1024;
    static readonly JsonNodeOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/bo/sessione", (HttpContext context, AdminAccess admin, SettingsStore settings) =>
        {
            var role = admin.Role(context);
            return Json(new JsonObject
            {
                ["autenticato"] = role != null,
                ["ruolo"] = role,
                ["installazione"] = admin.IsTechnician(context),
                ["passwordImpostata"] = admin.PasswordSet,
                ["tecnicoImpostato"] = admin.TechnicianSet,
                ["locale"] = AdminAccess.IsLocal(context),
                ["raggiungibile"] = admin.Reachable(context),
                ["demo"] = settings.Current.Installazione.Demo,
            });
        });

        app.MapPost("/api/bo/accesso", async (HttpContext context, AdminAccess admin, ILogger<AdminAccess> log) =>
        {
            var body = await Body(context);
            var error = admin.Login(context, Text(body, "password"));
            if (error == null)
            {
                log.LogInformation("Back office: entrato il {Role} da {Remote}", admin.Role(context) ?? "?", AgentApp.Remote(context));
                return Ok();
            }
            log.LogWarning("Back office: accesso rifiutato da {Remote} ({Error})", AgentApp.Remote(context), error);
            return Error(error, error == AdminAccess.OnlyFromThisPc ? 403 : error.StartsWith("Troppi", StringComparison.Ordinal) ? 429 : 401);
        });

        app.MapPost("/api/bo/esci", (HttpContext context, AdminAccess admin) =>
        {
            admin.EndSession(context);
            return Ok();
        });

        // La password di un ruolo. La prima (del titolare) solo dal PC dell'Agent; il titolare cambia la sua con quella
        // attuale; il tecnico imposta la sua e può reimpostare quella del titolare senza conoscerla.
        app.MapPost("/api/bo/password", async (HttpContext context, AdminAccess admin, SettingsStore settings, ILogger<AdminAccess> log) =>
        {
            var body = await Body(context);
            var next = Text(body, "nuova");
            if (AdminAccess.CheckNew(next) is { } weak)
                return Error(weak);
            var role = Text(body, "ruolo") == AccessEntry.Technician ? AccessEntry.Technician : AccessEntry.Owner;
            var first = !admin.PasswordSet;
            var session = admin.Role(context);
            if (first)
            {
                if (role != AccessEntry.Owner)
                    return Error("Prima la password del titolare.");
                if (!AdminAccess.IsLocal(context))
                    return Error("La prima password si imposta dal PC dell'Agent: apri http://localhost/backoffice lì.", 403);
            }
            else
            {
                if (session == null)
                    return Error("Accedi prima al back office.", 401);
                if (role == AccessEntry.Technician && !admin.IsTechnician(context))
                    return Error("La password del tecnico la cambia il tecnico.", 403);
                var current = settings.Current.Installazione.Password(role);
                // Chi cambia la propria password conferma quella di adesso; il tecnico reimposta quella del titolare.
                if (current != null && session == role && !AdminAccess.Verify(current, Text(body, "attuale") ?? ""))
                    return Error("La password attuale non è giusta.");
                if (current != null && session != role && role == AccessEntry.Technician)
                    return Error("La password del tecnico la cambia il tecnico.", 403);
            }
            var other = settings.Current.Installazione.Password(role == AccessEntry.Owner ? AccessEntry.Technician : AccessEntry.Owner);
            if (other != null && AdminAccess.Verify(other, next!))
                return Error("Titolare e tecnico hanno due password diverse: scegline un'altra.");
            var error = settings.Update(s =>
            {
                if (first && s.Installazione.Password(AccessEntry.Owner) != null)
                    return "La password è già stata impostata.";
                s.Installazione.SetPassword(role, AdminAccess.Hash(next!));
                return null;
            });
            if (error != null)
                return Error(error);
            admin.EndAllSessions(role);
            if (first || session == role)
                admin.StartSession(context, role);
            if (first)
                log.LogInformation("Password del titolare impostata");
            else
                log.LogInformation("Password del {Role} cambiata dal {Session} da {Remote}", role, session, AgentApp.Remote(context));
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
        // La sezione Installazione: per il titolare non c'è, nemmeno nelle API.
        var tech = bo.MapGroup("").AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (!context.RequestServices.GetRequiredService<AdminAccess>().IsTechnician(context))
                return Error("Questa parte è del tecnico.", 403);
            return await next(invocation);
        });

        // Lo stato completo, anche per l'assistenza (era /api/stato).
        app.MapGet("/api/stato", (HttpContext context, AdminAccess admin) =>
            admin.IsAuthenticated(context) ? Json(FullStatus(context)) : Error("Accedi al back office.", 401));
        bo.MapGet("/stato", (HttpContext context) => Json(FullStatus(context)));

        BrandApi.Map(bo);
        InstallationApi.Map(tech);

        bo.MapGet("/statistiche", (int? giorni, Statistics stats, SettingsStore settings) => Json(new JsonObject
        {
            ["giorni"] = stats.Report(giorni ?? 31),
            ["casse"] = new JsonArray(settings.Current.Casse.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["nome"] = c.Nome }).ToArray()),
        }));

        tech.MapGet("/log", (string? giorno, int? righe, AgentRuntime runtime) =>
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

        tech.MapGet("/https/autorita", (AgentRuntime runtime) =>
            runtime.AuthorityPath is { } path && File.Exists(path)
                ? Results.File(path, "application/x-x509-ca-cert", HttpsCertificate.AuthorityFile)
                : Error("Nessuna autorità: c'è solo con HTTPS attivo e il certificato generato dall'Agent.", 404));

        tech.MapPost("/riavvia", (IHostApplicationLifetime lifetime, Statistics stats, ILogger<AgentEngine> log) =>
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

    /// <summary>
    /// "Adesso": le casse con quello che stanno facendo e cosa va in onda in ogni posto, gli schermi abbinati e quelli che
    /// aspettano il codice, gli avvisi. Il tecnico vede anche i file, le codifiche e gli ultimi messaggi.
    /// </summary>
    static JsonObject FullStatus(HttpContext context)
    {
        var services = context.RequestServices;
        var engine = services.GetRequiredService<AgentEngine>();
        var settings = services.GetRequiredService<SettingsStore>();
        var runtime = services.GetRequiredService<AgentRuntime>();
        var devices = services.GetRequiredService<DeviceAccess>();
        var stats = services.GetRequiredService<Statistics>();
        var server = services.GetRequiredService<IServer>();
        var admin = services.GetRequiredService<AdminAccess>();
        var technician = admin.IsTechnician(context);
        var now = settings.Current;
        var clock = DateTime.Now;

        var status = engine.Status();
        status["servizio"] = WindowsServiceHelpers.IsWindowsService();
        status["riavvioNecessario"] = runtime.RestartNeeded(now);
        status["indirizzi"] = Addresses(server, runtime.StartupNetwork.ApertoInRete);
        status["demo"] = now.Installazione.Demo;
        status["ora"] = clock.ToString("yyyy-MM-ddTHH:mm:ss");
        foreach (var cash in status["casse"]!.AsArray().OfType<JsonObject>())
        {
            var id = cash["id"]!.GetValue<string>();
            var brand = now.BrandOf(now.CashPoint(id));
            var onAir = new JsonObject();
            foreach (var place in ShowcaseConfig.Places)
            {
                var choice = Showcases.Choose(brand, id, place, clock);
                onAir[place] = choice.Winner == null ? null : new JsonObject
                {
                    ["id"] = choice.Winner.Id,
                    ["nome"] = choice.Winner.Nome,
                    ["elementi"] = new JsonArray(choice.Items.Take(6).Select(i => (JsonNode)new JsonObject
                    {
                        ["id"] = i.Id,
                        ["nome"] = i.Name,
                        ["tipo"] = i.Promo != null ? "promozione" : i.Media!.IsVideo ? "video" : "foto",
                    }).ToArray()),
                    ["quanti"] = choice.Items.Count,
                };
            }
            cash["inOnda"] = onAir;
            if (!technician)
            {
                foreach (var key in new[] { "file", "fileLetti", "fileTolti", "codifica", "ultimiMessaggi", "modalita" })
                    cash.Remove(key);
            }
        }

        var notes = new JsonArray();
        if (technician)
        {
            status["cartellaDati"] = runtime.DataFolder;
            foreach (var note in settings.LoadNotes.Where(n => !n.StartsWith("Primo avvio", StringComparison.Ordinal)))
                notes.Add(note);
            if (runtime.HttpsProblem != null)
                notes.Add(runtime.HttpsProblem);
            foreach (var problem in runtime.StartupProblems)
                notes.Add(problem);
            if (stats.LoadProblem != null)
                notes.Add(stats.LoadProblem);
            status["https"] = runtime.Certificate is { } certificate
                ? new JsonObject
                {
                    ["soggetto"] = certificate.Subject,
                    ["scade"] = certificate.NotAfter.ToString("O"),
                    ["generato"] = runtime.AuthorityPath != null,
                }
                : null;
        }
        else
        {
            status.Remove("copieCartacee");
        }
        foreach (var old in engine.OldMonitors)
            notes.Add($"Il vecchio MonitorCliente.exe è aperto ({old}): va chiuso e tolto dall'avvio automatico, l'Agent toglie il file che legge.");
        status["avvisi"] = notes;

        var online = engine.ConnectedDevices();
        status["schermi"] = new JsonArray(now.Schermi.Select(d =>
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

    internal static HashSet<int> BoundPorts(IServer server) =>
        (server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [])
            .Select(a => Uri.TryCreate(a.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"), UriKind.Absolute, out var uri) ? uri.Port : -1)
            .ToHashSet();

    /// <summary>Gli indirizzi da aprire sui dispositivi: con l'Agent aperto in rete, quelli del PC; altrimenti localhost.</summary>
    internal static JsonArray Addresses(IServer server, bool open)
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

    internal static async Task<JsonObject?> Body(HttpContext context, int limit = MaxBody)
    {
        if (context.Request.ContentLength > limit)
            return null;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var buffer = new char[limit + 1];
        var length = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        if (length > limit)
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

    internal static async Task<T?> Read<T>(HttpContext context, int limit = MaxBody) where T : class
    {
        var body = await Body(context, limit);
        return As<T>(body);
    }

    internal static T? As<T>(JsonNode? node) where T : class
    {
        try
        {
            return node?.Deserialize<T>(AgentSettings.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? Text(JsonObject? body, string name) =>
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

    /// <summary>Un testo che arriva dal back office: senza spazi in più, mai null.</summary>
    internal static string Clean(string? text) => (text ?? "").Trim();

    /// <summary>Un testo facoltativo: null se vuoto.</summary>
    internal static string? Optional(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
