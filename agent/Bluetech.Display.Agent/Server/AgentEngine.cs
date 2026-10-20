using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Stampa;
using Bluetech.Display.Agent.Vendita;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Tiene insieme la lettura del file, la vendita e i display collegati. Un solo lucchetto per tracker e invio: un
/// display che si collega riceve lo stato completo e poi tutti i messaggi successivi, senza buchi né doppioni.
/// </summary>
public sealed partial class AgentEngine(
    IOptions<AgentOptions> options,
    DisplayHub hub,
    ILogger<AgentEngine> log,
    ILoggerFactory loggers) : BackgroundService
{
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(3);
    static readonly TimeSpan StartRetry = TimeSpan.FromSeconds(10);
    const int RecentLimit = 50;
    const int PrintsLimit = 20;

    readonly object _gate = new();
    readonly SaleTracker _tracker = new(DigitalSettings(options.Value));
    readonly ICopyPrinter? _printer = Printer(options.Value);
    readonly Queue<JsonObject> _recent = new();
    readonly Queue<JsonObject> _prints = new();
    readonly DateTimeOffset _started = DateTimeOffset.Now;
    readonly long _startedTicks = Stopwatch.GetTimestamp();
    volatile CashFileSource? _source;
    volatile string? _problem;
    string? _lastEncoding;
    string[] _oldMonitors = [];

    AgentOptions Options => options.Value;

    /// <summary>
    /// L'ora per i tempi della vendita e del battito: monotona, così una correzione dell'orologio di Windows non ferma
    /// il battito e non chiude un conto. Parte dall'ora vera di avvio.
    /// </summary>
    DateTimeOffset Now => _started + Stopwatch.GetElapsedTime(_startedTicks);
    TimeSpan CloseAfter => TimeSpan.FromSeconds(Math.Max(0, Options.ChiusuraVenditaDopoSecondi));
    bool Replaying => !string.IsNullOrWhiteSpace(Options.Replay);
    PaperCopyOptions PrintOptions => Options.ScontrinoDigitale.Stampa;

    /// <summary>La stampante della copia cartacea, solo se scontrino digitale e copia sono attivi e configurati.</summary>
    static ICopyPrinter? Printer(AgentOptions options) =>
        options.ScontrinoDigitale is { MostraScontrino: true, CopiaCartacea: true } digital
            ? CopyPrinters.Create(digital.Stampa, Path.Combine(AppContext.BaseDirectory, options.CartellaLog))
            : null;

    static DigitalReceiptSettings DigitalSettings(AgentOptions options) => new(
        options.ScontrinoDigitale.MostraScontrino,
        Printer(options) != null,
        TimeSpan.FromSeconds(Math.Max(5, options.ScontrinoDigitale.SecondiAVideo)),
        options.ScontrinoDigitale.Stampa.Dicitura);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await RunAsync(stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && WindowsServiceHelpers.IsWindowsService())
        {
            // Un servizio che si ferma da solo risulta fermato normalmente e Windows non lo riavvia: si esce con un
            // codice d'errore, così valgono le azioni di ripristino ("sc failure", nel README).
            log.LogCritical(ex, "Errore imprevisto: l'Agent si chiude e Windows lo riavvia");
            Environment.Exit(1);
        }
    }

    async Task RunAsync(CancellationToken stop)
    {
        _oldMonitors = FindOldMonitors();
        if (_oldMonitors.Length > 0)
            log.LogWarning("Il vecchio MonitorCliente.exe è aperto ({Paths}): legge lo stesso file e non vedrà più i conti, perché l'Agent li toglie. Va chiuso e tolto dall'avvio automatico", string.Join(", ", _oldMonitors));

        var background = Replaying
            ? Task.Run(() => RunReplay(Options.Replay!, stop), stop)
            : Task.Run(() => StartSource(stop), stop);

        TimeSpan? lastBeat = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                lock (_gate)
                    Publish(_tracker.CheckIdle(Now, CloseAfter));
                var elapsed = Stopwatch.GetElapsedTime(_startedTicks);
                if (lastBeat == null || elapsed - lastBeat >= HeartbeatEvery)
                {
                    hub.Broadcast([new Heartbeat()]);
                    lastBeat = elapsed;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                await background;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Errore alla chiusura");   // allo stop non si esce con un codice d'errore
            }
            _source?.Dispose();
        }
    }

    /// <summary>
    /// Comincia a sorvegliare il file. Se non si può (cartella su un disco di rete che il servizio non vede, permessi),
    /// l'Agent resta acceso, lo dice in /api/stato e riprova ogni 10 s: il servizio non si ferma per un percorso sbagliato.
    /// </summary>
    async Task StartSource(CancellationToken stop)
    {
        for (var attempt = 1; !stop.IsCancellationRequested; attempt++)
        {
            CashFileSource? source = null;
            try
            {
                if (string.IsNullOrWhiteSpace(Options.FileCassa))
                    throw new ArgumentException("in appsettings.json manca FileCassa.");
                source = new CashFileSource(Options.FileCassa, CloseAfter, loggers.CreateLogger<CashFileSource>(), OnFile);
                source.Start();
                _source = source;
                _problem = null;
                if (attempt > 1)
                    log.LogInformation("Sorveglianza di {Path} partita al tentativo {Attempt}", source.FilePath, attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                source?.Dispose();
                _problem = $"Non riesco a sorvegliare {Options.FileCassa}: {ex.Message} Riprovo ogni 10 secondi.";
                if (attempt == 1)
                    log.LogError(ex, "Non riesco a sorvegliare {Path}: riprovo ogni 10 s (da servizio i dischi di rete collegati con una lettera non si vedono)", Options.FileCassa);
            }
            await Task.Delay(StartRetry, stop);
        }
    }

    /// <summary>Una versione del file, letta e già tolta dalla cartella.</summary>
    public void OnFile(byte[] bytes)
    {
        var file = ReceiptParser.Parse(bytes);
        lock (_gate)
        {
            if (file.Encoding != _lastEncoding)
                log.LogInformation("Codifica del file: {Encoding}", file.Encoding);
            _lastEncoding = file.Encoding;
            Publish(_tracker.Apply(file, Now, DateTimeOffset.Now));
        }
    }

    /// <summary>Un comando dal display. L'unico è la richiesta della copia cartacea dello scontrino digitale.</summary>
    public async Task OnDisplayMessage(DisplayClient client, JsonObject message)
    {
        if (message["t"] is not JsonValue type || !type.TryGetValue<string>(out var t) || t != "receipt.paper"
            || message["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id))
        {
            log.LogDebug("Display {Remote}: comando sconosciuto, ignorato", client.Remote);
            return;
        }
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref client.LastCommandAt) < 2000)
            return;   // tocchi ripetuti
        Interlocked.Exchange(ref client.LastCommandAt, now);
        await PrintPaperCopy(client, id);
    }

    /// <summary>
    /// Stampa la copia dello scontrino digitale <paramref name="id"/>: una sola volta (dopo un errore si può riprovare),
    /// entro <see cref="PaperCopyOptions.TimeoutSecondi"/>, e dice a tutti i display com'è andata.
    /// </summary>
    async Task PrintPaperCopy(DisplayClient client, string id)
    {
        DigitalReceipt receipt;
        ICopyPrinter printer;
        lock (_gate)
        {
            if (_tracker.Receipt is not { } current || current.Id != id || !current.PaperCopy || _printer is not { } configured)
            {
                hub.Send(client, new PaperStatus(id, "failed", "Lo scontrino non è più a video"));
                return;
            }
            if (current.Paper is PaperState.Printing or PaperState.Requested)
            {
                hub.Send(client, new PaperStatus(id, "already"));
                return;
            }
            receipt = current;
            printer = configured;
            _tracker.SetPaper(id, PaperState.Printing);
            Publish([new PaperStatus(id, "printing")]);
        }

        var blocks = PaperCopy.Compose(receipt, PrintOptions);
        var watch = Stopwatch.StartNew();
        string? problem = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(PrintOptions.TimeoutSecondi, 1, 60)));
            await printer.PrintAsync(EscPos.Encode(blocks, PrintOptions), PaperCopy.ToText(blocks, PrintOptions.Colonne), id, timeout.Token);
        }
        catch (Exception ex)
        {
            problem = ex is OperationCanceledException ? "la stampante non risponde" : ex.Message;
            log.LogWarning("Copia cartacea {Id} non stampata su {Printer}: {Problem}", id, printer.Description, problem);
        }

        lock (_gate)
        {
            // Lo scontrino può essere già sparito dallo schermo: l'esito vale lo stesso per chi l'ha chiesto.
            _tracker.SetPaper(id, problem == null ? PaperState.Requested : PaperState.Failed);
            Publish([problem == null ? new PaperStatus(id, "requested", PrintOptions.TestoRitiro) : new PaperStatus(id, "failed")]);
            _prints.Enqueue(new JsonObject
            {
                ["ora"] = DateTimeOffset.Now.ToString("HH:mm:ss"),
                ["id"] = id,
                ["display"] = client.Remote,
                ["esito"] = problem == null ? "inviata" : "non stampata",
                ["ms"] = watch.ElapsedMilliseconds,
                ["problema"] = problem,
                ["soloQr"] = receipt.Bill == null,
            });
            while (_prints.Count > PrintsLimit)
                _prints.Dequeue();
        }
        if (problem == null)
            log.LogInformation("Copia cartacea {Id} inviata a {Printer} in {Ms} ms", id, printer.Description, watch.ElapsedMilliseconds);
    }

    /// <summary>Un display si collega: stato completo subito, poi i messaggi.</summary>
    public DisplayClient Connect(WebSocket socket, string remote)
    {
        lock (_gate)
        {
            var client = hub.Add(socket, remote);
            hub.Send(client, new Sync(_tracker.Snapshot()));
            return client;
        }
    }

    void Publish(IReadOnlyList<CashIn> messages)
    {
        if (messages.Count == 0)
            return;
        hub.Broadcast(messages);
        foreach (var message in messages)
        {
            var json = message.ToJson();
            if (json["png"] is not null)
                json["png"] = "(immagine)";   // il QR nel log e in /api/stato non serve
            if (json["state"] is JsonObject state && state["payment"] is JsonObject payment)
                payment["png"] = "(immagine)";
            if (json["state"] is JsonObject withReceipt && withReceipt["receipt"] is JsonObject receipt)
                receipt["png"] = "(immagine)";
            json["ora"] = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
            _recent.Enqueue(json);
            while (_recent.Count > RecentLimit)
                _recent.Dequeue();
            log.LogDebug("→ {Message}", json.ToJsonString());
        }
    }

    public JsonObject Status()
    {
        lock (_gate)
        {
            return new JsonObject
            {
                ["versione"] = typeof(AgentEngine).Assembly.GetName().Version?.ToString(),
                ["avviato"] = _started.ToString("O"),
                ["modalita"] = Replaying ? "replay" : "cassa",
                ["file"] = Replaying ? Options.Replay : _source?.FilePath ?? Options.FileCassa,
                ["fileLetti"] = _source?.FilesRead ?? 0,
                ["fileTolti"] = _source?.FilesRemoved ?? 0,
                ["ultimoFile"] = _source?.LastFileAt?.ToString("O"),
                ["problema"] = _problem ?? _source?.LastProblem,
                ["codifica"] = _lastEncoding,
                ["vecchioMonitor"] = new JsonArray(_oldMonitors.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
                ["display"] = hub.ClientCount,
                ["messaggiInviati"] = hub.MessagesSent,
                ["fase"] = _tracker.Phase,
                ["totale"] = _tracker.Total,
                ["righe"] = _tracker.Lines.Count,
                ["scontrinoDigitale"] = new JsonObject
                {
                    ["attivo"] = Options.ScontrinoDigitale.MostraScontrino,
                    ["copiaCartacea"] = _printer != null,
                    ["stampante"] = _printer?.Description,
                    ["aVideo"] = _tracker.Receipt?.Id,
                    ["conConto"] = _tracker.Receipt is { } shown ? shown.Bill != null : null,
                },
                ["copieCartacee"] = new JsonArray(_prints.Select(p => (JsonNode)p.DeepClone()).ToArray()),
                ["ultimiMessaggi"] = new JsonArray(_recent.Select(m => (JsonNode)m.DeepClone()).ToArray()),
            };
        }
    }

    async Task RunReplay(string folder, CancellationToken stop)
    {
        try
        {
            await Replay(folder, stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _problem = $"Replay interrotto: {ex.Message}";
            log.LogError(ex, "Replay interrotto");
        }
    }

    async Task Replay(string folder, CancellationToken stop)
    {
        if (!Directory.Exists(folder))
        {
            _problem = $"Replay: la cartella {folder} non esiste";
            log.LogError("Replay: la cartella {Folder} non esiste", folder);
            return;
        }
        var steps = LoadReplay(folder);
        if (steps.Count == 0)
        {
            _problem = $"Replay: nessuna versione in {folder}";
            log.LogError("Replay: nessuna versione in {Folder}", folder);
            return;
        }
        var speed = Math.Clamp(Options.VelocitaReplay, 0.1, 100);
        log.LogInformation("Replay di {Count} versioni da {Folder} (velocità {Speed})", steps.Count, folder, speed);
        do
        {
            long? previous = null;
            foreach (var (file, ms) in steps)
            {
                if (previous.HasValue)
                {
                    // Le pause lunghe in cassa (un tavolo che aspetta) si accorciano a 10 s.
                    var gap = Math.Clamp(ms - previous.Value, 0, 10_000) / speed;
                    await Task.Delay(TimeSpan.FromMilliseconds(gap), stop);
                }
                previous = ms;
                OnFile(await File.ReadAllBytesAsync(file, stop));
            }
            await Task.Delay(TimeSpan.FromSeconds(5 / speed), stop);
            lock (_gate)
                Publish(_tracker.CloseNow());
        }
        while (Options.RipetiReplay && !stop.IsCancellationRequested);
        log.LogInformation("Replay finito");
    }

    [GeneratedRegex(@"^\d{4}_.*\.txt$")]
    private static partial Regex VersionFile();

    internal static List<(string File, long Ms)> LoadReplay(string folder)
    {
        var steps = new List<(string File, long Ms)>();
        var index = Path.Combine(folder, "eventi.jsonl");
        if (File.Exists(index))
        {
            foreach (var line in File.ReadLines(index))
            {
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }
                if (node?["tipo"]?.GetValue<string>() == "versione" && node["file"]?.GetValue<string>() is { } name)
                    steps.Add((Path.Combine(folder, name), node["ms"]?.GetValue<long>() ?? 0));
            }
        }
        if (steps.Count == 0)
        {
            steps = Directory.GetFiles(folder)
                .Where(f => VersionFile().IsMatch(Path.GetFileName(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .Select((f, i) => (f, i * 1000L))
                .ToList();
        }
        return steps.Where(s => File.Exists(s.File)).ToList();
    }

    static string[] FindOldMonitors()
    {
        try
        {
            return Process.GetProcessesByName("MonitorCliente")
                .Select(p =>
                {
                    using (p)
                    {
                        try
                        {
                            return p.MainModule?.FileName ?? "MonitorCliente.exe";
                        }
                        catch (Exception)
                        {
                            return "MonitorCliente.exe";
                        }
                    }
                })
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
