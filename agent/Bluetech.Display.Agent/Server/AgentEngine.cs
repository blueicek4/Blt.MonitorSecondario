using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Bluetech.Display.Agent.Stampa;
using Bluetech.Display.Agent.Vendita;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace Bluetech.Display.Agent.Server;

/// <summary>
/// Tiene insieme le casse (lettura del file e vendita), i display collegati e le stampanti della copia cartacea. Un
/// solo lucchetto per tracker e invio: un display che si collega riceve lo stato completo della sua cassa e poi tutti i
/// messaggi successivi, senza buchi né doppioni. Le impostazioni cambiate dal back office valgono subito.
/// </summary>
public sealed partial class AgentEngine(
    IOptions<AgentOptions> options,
    SettingsStore settings,
    DisplayHub hub,
    Statistics stats,
    ILogger<AgentEngine> log,
    ILoggerFactory loggers) : BackgroundService
{
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(3);
    static readonly TimeSpan StartRetry = TimeSpan.FromSeconds(10);
    static readonly TimeSpan SaveStatsEvery = TimeSpan.FromMinutes(1);
    /// <summary>Ogni quanto si guarda se un set di reclame entra o esce dal suo orario.</summary>
    static readonly TimeSpan ScheduleEvery = TimeSpan.FromSeconds(30);
    const int RecentLimit = 50;
    const int PrintsLimit = 30;

    /// <summary>Una cassa: il suo file, la sua vendita, i suoi ultimi messaggi.</summary>
    sealed class CashPoint(string id, SaleTracker tracker)
    {
        public string Id { get; } = id;
        public string Name = "";
        public string File = "";
        public SaleTracker Tracker = tracker;
        public CashFileSource? Source;
        public string? Problem;
        public string? LastEncoding;
        /// <summary>Cresce quando la cassa si ferma o cambia file: le letture del file di prima non valgono più.</summary>
        public int Generation;
        /// <summary>Al posto del file, la registrazione di --replay.</summary>
        public bool Replay;
        public CancellationTokenSource? Starting;
        public readonly Queue<JsonObject> Recent = new();
        /// <summary>L'aspetto mandato ai display della cassa (tema, set di reclame): a chi si collega subito, agli altri quando cambia.</summary>
        public ConfigMessage? Config;
        public string? ConfigJson;

        /// <summary>Ferma la sorveglianza; la sorgente si chiude fuori dal lucchetto (può aspettare una lettura in corso).</summary>
        public CashFileSource? Retire()
        {
            Generation++;
            try
            {
                Starting?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            Starting = null;
            var source = Source;
            Source = null;
            return source;
        }
    }

    readonly object _gate = new();
    readonly List<CashPoint> _points = [];
    readonly Queue<JsonObject> _prints = new();
    readonly DateTimeOffset _started = DateTimeOffset.Now;
    readonly long _startedTicks = Stopwatch.GetTimestamp();
    CancellationToken _stop;
    string[] _oldMonitors = [];
    readonly Dictionary<string, string?> _qr = [];

    AgentOptions Options => options.Value;
    string LogFolder => Path.Combine(AppContext.BaseDirectory, Options.CartellaLog);

    /// <summary>
    /// L'ora per i tempi della vendita e del battito: monotona, così una correzione dell'orologio di Windows non ferma
    /// il battito e non chiude un conto. Parte dall'ora vera di avvio.
    /// </summary>
    DateTimeOffset Now => _started + Stopwatch.GetElapsedTime(_startedTicks);
    TimeSpan CloseAfter => TimeSpan.FromSeconds(Math.Max(0, settings.Current.ChiusuraVenditaDopoSecondi));
    bool Replaying => !string.IsNullOrWhiteSpace(Options.Replay);
    public DateTimeOffset Started => _started;
    public IReadOnlyList<string> OldMonitors => _oldMonitors;

    static DigitalReceiptSettings DigitalSettings(AgentSettings s) => new(
        s.ScontrinoDigitale.MostraScontrino,
        s.ScontrinoDigitale.MostraScontrino && s.ScontrinoDigitale.CopiaCartacea,
        TimeSpan.FromSeconds(Math.Max(5, s.ScontrinoDigitale.SecondiAVideo)),
        s.ScontrinoDigitale.Dicitura);

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
        _stop = stop;
        _oldMonitors = FindOldMonitors();
        if (_oldMonitors.Length > 0)
            log.LogWarning("Il vecchio MonitorCliente.exe è aperto ({Paths}): legge lo stesso file e non vedrà più i conti, perché l'Agent li toglie. Va chiuso e tolto dall'avvio automatico", string.Join(", ", _oldMonitors));

        List<CashFileSource> retired;
        lock (_gate)
        {
            retired = Apply(null, settings.Current);
            settings.Changed += OnSettingsChanged;
        }
        Close(retired);

        var replay = Replaying ? Task.Run(() => RunReplay(Options.Replay!, stop), stop) : Task.CompletedTask;
        TimeSpan? lastBeat = null;
        var lastSave = TimeSpan.Zero;
        var lastSchedule = TimeSpan.Zero;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var closeAfter = CloseAfter;
                lock (_gate)
                {
                    foreach (var point in _points)
                        Publish(point, point.Tracker.CheckIdle(Now, closeAfter));
                }
                var elapsed = Stopwatch.GetElapsedTime(_startedTicks);
                if (lastBeat == null || elapsed - lastBeat >= HeartbeatEvery)
                {
                    hub.BroadcastAll(new Heartbeat());
                    lastBeat = elapsed;
                }
                if (elapsed - lastSave >= SaveStatsEvery)
                {
                    stats.SaveIfDirty();
                    lastSave = elapsed;
                }
                if (elapsed - lastSchedule >= ScheduleEvery)
                {
                    var current = settings.Current;
                    lock (_gate)
                    {
                        foreach (var point in _points)
                            RefreshConfig(point, current);
                    }
                    lastSchedule = elapsed;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            settings.Changed -= OnSettingsChanged;
            try
            {
                await replay;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Errore alla chiusura");   // allo stop non si esce con un codice d'errore
            }
            lock (_gate)
                retired = _points.Select(p => p.Retire()).OfType<CashFileSource>().ToList();
            Close(retired);
            stats.SaveIfDirty();
        }
    }

    void OnSettingsChanged(AgentSettings before, AgentSettings after)
    {
        List<CashFileSource> retired;
        lock (_gate)
            retired = Apply(before, after);
        Close(retired);
    }

    /// <summary>
    /// Porta le casse alle impostazioni: aggiunge, toglie, cambia file; aggiorna lo scontrino digitale; stacca i display
    /// dei dispositivi revocati o spostati, che si ricollegano con le impostazioni nuove. Dentro il lucchetto.
    /// </summary>
    List<CashFileSource> Apply(AgentSettings? before, AgentSettings now)
    {
        var retired = new List<CashFileSource>();
        var digital = DigitalSettings(now);
        foreach (var point in _points.ToList())
        {
            if (now.CashPoint(point.Id) != null)
                continue;
            if (point.Retire() is { } source)
                retired.Add(source);
            _points.Remove(point);
            hub.Disconnect(c => c.CashPoint == point.Id);
            log.LogInformation("{Name} tolta: il suo file non si legge più", point.Name);
        }

        var ordered = new List<CashPoint>();
        foreach (var config in now.Casse)
        {
            var point = _points.Find(p => p.Id == config.Id);
            if (point == null)
            {
                point = new CashPoint(config.Id, new SaleTracker(digital))
                {
                    Name = config.Nome,
                    Replay = Replaying && before == null && ordered.Count == 0,
                };
                Start(point, config.FileCassa);
                if (before != null)
                    log.LogInformation("{Name} aggiunta: {File}", config.Nome, config.FileCassa);
            }
            else if (!string.Equals(point.File, config.FileCassa, StringComparison.OrdinalIgnoreCase))
            {
                if (point.Retire() is { } source)
                    retired.Add(source);
                Publish(point, point.Tracker.CloseNow());   // la vendita del file di prima non continua nel nuovo
                point.Tracker = new SaleTracker(digital);
                point.LastEncoding = null;
                Start(point, config.FileCassa);
                log.LogInformation("{Name}: ora il file è {File}", config.Nome, config.FileCassa);
            }
            point.Name = config.Nome;
            point.Tracker.Digital = digital;
            ordered.Add(point);
        }
        _points.Clear();
        _points.AddRange(ordered);
        foreach (var point in _points)
            RefreshConfig(point, now);

        if (before != null)
        {
            foreach (var old in before.Dispositivi)
            {
                var current = now.Device(old.Id);
                if (current == null || current.Cassa != old.Cassa || current.Stampante != old.Stampante || current.Token != old.Token)
                    hub.Disconnect(c => c.DeviceId == old.Id);
            }
        }
        return retired;
    }

    static void Close(List<CashFileSource> sources)
    {
        foreach (var source in sources)
            source.Dispose();
    }

    void Start(CashPoint point, string file)
    {
        point.File = file;
        point.Problem = null;
        if (point.Replay)
            return;
        var generation = point.Generation;
        var starting = CancellationTokenSource.CreateLinkedTokenSource(_stop);
        point.Starting = starting;
        _ = Task.Run(async () =>
        {
            try
            {
                await StartSource(point, generation, file, starting.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                starting.Dispose();
            }
        });
    }

    /// <summary>
    /// Comincia a sorvegliare il file di una cassa. Se non si può (cartella su un disco di rete che il servizio non vede,
    /// permessi), l'Agent resta acceso, lo dice nel back office e riprova ogni 10 s.
    /// </summary>
    async Task StartSource(CashPoint point, int generation, string file, CancellationToken stop)
    {
        for (var attempt = 1; !stop.IsCancellationRequested; attempt++)
        {
            CashFileSource? source = null;
            try
            {
                source = new CashFileSource(file, CloseAfter, loggers.CreateLogger<CashFileSource>(), bytes => OnFile(point, generation, bytes));
                source.Start();
                bool current;
                lock (_gate)
                {
                    current = point.Generation == generation;
                    if (current)
                    {
                        point.Source = source;
                        point.Problem = null;
                    }
                }
                if (!current)
                {
                    source.Dispose();   // la cassa è stata tolta o ha cambiato file nel frattempo
                    return;
                }
                if (attempt > 1)
                    log.LogInformation("{Name}: sorveglianza di {Path} partita al tentativo {Attempt}", point.Name, source.FilePath, attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                source?.Dispose();
                lock (_gate)
                {
                    if (point.Generation == generation)
                        point.Problem = $"Non riesco a sorvegliare {file}: {ex.Message} Riprovo ogni 10 secondi.";
                }
                if (attempt == 1)
                    log.LogError(ex, "{Name}: non riesco a sorvegliare {Path}: riprovo ogni 10 s (da servizio i dischi di rete collegati con una lettera non si vedono)", point.Name, file);
            }
            await Task.Delay(StartRetry, stop);
        }
    }

    /// <summary>Una versione del file di una cassa, letta e già tolta dalla cartella.</summary>
    void OnFile(CashPoint point, int generation, byte[] bytes)
    {
        var file = ReceiptParser.Parse(bytes);
        lock (_gate)
        {
            if (point.Generation != generation)
                return;   // cassa tolta o file cambiato mentre si leggeva
            if (file.Encoding != point.LastEncoding)
                log.LogInformation("{Name}: codifica del file {Encoding}", point.Name, file.Encoding);
            point.LastEncoding = file.Encoding;
            stats.FileRead(point.Id);
            Publish(point, point.Tracker.Apply(file, Now, DateTimeOffset.Now));
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
    /// Stampa la copia dello scontrino digitale <paramref name="id"/> sulla stampante del dispositivo che l'ha chiesta:
    /// una sola volta per scontrino (dopo un errore si può riprovare), entro il tempo della stampante, e dice a tutti i
    /// display della cassa com'è andata.
    /// </summary>
    async Task PrintPaperCopy(DisplayClient client, string id)
    {
        DigitalReceipt receipt;
        CashPoint point;
        PrinterConfig printerConfig;
        PaperCopyOptions printOptions;
        ICopyPrinter? printer;
        lock (_gate)
        {
            var now = settings.Current;
            var configured = now.Printer(now.Device(client.DeviceId)?.Stampante);
            var found = _points.Find(p => p.Id == client.CashPoint);
            if (found?.Tracker.Receipt is not { } current || current.Id != id || !current.PaperCopy)
            {
                hub.Send(client, new PaperStatus(id, "failed", "Lo scontrino non è più a video"));
                return;
            }
            if (configured == null)
            {
                hub.Send(client, new PaperStatus(id, "failed", "Questo schermo non ha una stampante: chiedi la copia alla cassa"));
                return;
            }
            if (current.Paper is PaperState.Printing or PaperState.Requested)
            {
                hub.Send(client, new PaperStatus(id, "already"));
                return;
            }
            point = found;
            receipt = current;
            printerConfig = configured;
            printOptions = configured.ToOptions(now.ScontrinoDigitale.Dicitura);
            printer = CopyPrinters.Create(printOptions, LogFolder);
            point.Tracker.SetPaper(id, PaperState.Printing);
            Publish(point, [new PaperStatus(id, "printing")]);
        }

        var watch = Stopwatch.StartNew();
        string? problem = null;
        try
        {
            if (printer == null)
                throw new InvalidOperationException($"la stampante {printerConfig.Nome} non ha l'indirizzo");
            var blocks = PaperCopy.Compose(receipt, printOptions);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(printOptions.TimeoutSecondi, 1, 60)));
            await printer.PrintAsync(EscPos.Encode(blocks, printOptions), PaperCopy.ToText(blocks, printOptions.Colonne), id, timeout.Token);
        }
        catch (Exception ex)
        {
            problem = ex is OperationCanceledException ? "la stampante non risponde" : ex.Message;
            log.LogWarning("Copia cartacea {Id} non stampata su {Printer}: {Problem}", id, printer?.Description ?? printerConfig.Nome, problem);
        }

        lock (_gate)
        {
            // Lo scontrino può essere già sparito dallo schermo: l'esito vale lo stesso per chi l'ha chiesto.
            point.Tracker.SetPaper(id, problem == null ? PaperState.Requested : PaperState.Failed);
            Publish(point, [problem == null ? new PaperStatus(id, "requested", printOptions.TestoRitiro) : new PaperStatus(id, "failed")]);
            stats.PaperCopy(point.Id, client.DeviceId, problem == null);
            _prints.Enqueue(new JsonObject
            {
                ["ora"] = DateTimeOffset.Now.ToString("O"),
                ["id"] = id,
                ["cassa"] = point.Name,
                ["dispositivo"] = client.Name,
                ["stampante"] = printerConfig.Nome,
                ["esito"] = problem == null ? "inviata" : "non stampata",
                ["ms"] = watch.ElapsedMilliseconds,
                ["problema"] = problem,
                ["soloQr"] = receipt.Bill == null,
            });
            while (_prints.Count > PrintsLimit)
                _prints.Dequeue();
        }
        if (problem == null)
            log.LogInformation("Copia cartacea {Id} di {Device} inviata a {Printer} in {Ms} ms", id, client.Name, printer!.Description, watch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Un display si collega: stato completo della sua cassa subito, poi i messaggi. <paramref name="deviceId"/> è il
    /// dispositivo autorizzato, oppure null con <paramref name="previewCashPoint"/> per l'anteprima dal back office.
    /// Null se il dispositivo o la cassa non ci sono (più).
    /// </summary>
    public DisplayClient? Connect(WebSocket socket, string remote, string? deviceId, string? previewCashPoint)
    {
        lock (_gate)
        {
            // Le impostazioni si leggono qui dentro: un dispositivo spostato adesso viene staccato subito dopo da Apply.
            var now = settings.Current;
            string cashPoint;
            string name;
            var canPrint = false;
            if (deviceId != null)
            {
                if (now.Device(deviceId) is not { } device)
                    return null;
                cashPoint = device.Cassa;
                name = device.Nome;
                canPrint = now.Printer(device.Stampante) != null;
            }
            else
            {
                cashPoint = previewCashPoint ?? _points.FirstOrDefault()?.Id ?? "";
                name = "anteprima del back office";
            }
            if (_points.Find(p => p.Id == cashPoint) is not { } point)
                return null;
            var client = hub.Add(socket, remote, cashPoint, deviceId, name, canPrint);
            if (point.Config != null)
                hub.Send(client, point.Config);   // prima l'aspetto, poi il conto: lo schermo non cambia faccia dopo
            hub.Send(client, new Sync(point.Tracker.Snapshot()));
            return client;
        }
    }

    /// <summary>
    /// Ricompone l'aspetto della cassa (impostazioni cambiate, o un set che entra o esce dal suo orario) e lo
    /// manda ai suoi display solo se è cambiato. Dentro il lucchetto.
    /// </summary>
    void RefreshConfig(CashPoint point, AgentSettings now)
    {
        JsonObject body;
        var clock = DateTime.Now;   // lo stesso istante per il messaggio e per il log
        try
        {
            body = DisplayConfig.Build(now, point.Id, clock, CachedQr);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogError(ex, "{Name}: aspetto non composto, i display tengono quello di prima", point.Name);
            return;
        }
        var json = body.ToJsonString();
        if (json == point.ConfigJson)
            return;
        var first = point.ConfigJson == null;
        point.ConfigJson = json;
        point.Config = new ConfigMessage(body);
        if (!first)
        {
            hub.Broadcast(point.Id, [point.Config]);
            var onAir = DisplayConfig.OnAir(now, point.Id, clock);
            string Set(string place) => onAir[place]?.Nome ?? "niente";
            log.LogInformation("{Name}: aspetto aggiornato sui display (attesa: {Idle}, accanto al conto: {Sale}, Grazie: {Thanks})",
                point.Name, Set("attesa"), Set("conto"), Set("grazie"));
        }
    }

    /// <summary>Il PNG del QR di una promozione, fatto una volta per indirizzo. Null se l'indirizzo non entra in un QR.</summary>
    public string? CachedQr(string url)
    {
        lock (_qr)
        {
            if (_qr.TryGetValue(url, out var png))
                return png;
            try
            {
                png = DisplayConfig.QrPng(url);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                log.LogWarning("QR non creato per {Url}: {Problem}", url, ex.Message);
                png = null;
            }
            if (_qr.Count >= 250)   // 200 promozioni al massimo, più l'anteprima
                _qr.Clear();
            return _qr[url] = png;
        }
    }

    void Publish(CashPoint point, IReadOnlyList<CashIn> messages)
    {
        if (messages.Count == 0)
            return;
        hub.Broadcast(point.Id, messages);
        stats.Count(point.Id, messages);
        foreach (var message in messages)
        {
            var json = message.ToJson();
            if (json["png"] is not null)
                json["png"] = "(immagine)";   // il QR nel log e nel back office non serve
            if (json["state"] is JsonObject state && state["payment"] is JsonObject payment)
                payment["png"] = "(immagine)";
            if (json["state"] is JsonObject withReceipt && withReceipt["receipt"] is JsonObject receipt)
                receipt["png"] = "(immagine)";
            json["ora"] = DateTimeOffset.Now.ToString("HH:mm:ss.fff");
            point.Recent.Enqueue(json);
            while (point.Recent.Count > RecentLimit)
                point.Recent.Dequeue();
            log.LogDebug("{Name} → {Message}", point.Name, json.ToJsonString());
        }
    }

    public JsonObject Status()
    {
        lock (_gate)
        {
            var clients = hub.Clients;
            var casse = new JsonArray();
            foreach (var point in _points)
            {
                casse.Add(new JsonObject
                {
                    ["id"] = point.Id,
                    ["nome"] = point.Name,
                    ["modalita"] = point.Replay ? "replay" : "cassa",
                    ["file"] = point.Replay ? Options.Replay : point.Source?.FilePath ?? point.File,
                    ["fileLetti"] = point.Source?.FilesRead ?? 0,
                    ["fileTolti"] = point.Source?.FilesRemoved ?? 0,
                    ["ultimoFile"] = point.Source?.LastFileAt?.ToString("O"),
                    ["problema"] = point.Problem ?? point.Source?.LastProblem,
                    ["codifica"] = point.LastEncoding,
                    ["fase"] = point.Tracker.Phase,
                    ["totale"] = point.Tracker.Total,
                    ["righe"] = point.Tracker.Lines.Count,
                    ["display"] = clients.Count(c => c.CashPoint == point.Id),
                    ["scontrinoDigitale"] = point.Tracker.Receipt is { } shown
                        ? new JsonObject { ["id"] = shown.Id, ["conConto"] = shown.Bill != null, ["copia"] = shown.Paper.ToString().ToLowerInvariant() }
                        : null,
                    ["ultimiMessaggi"] = new JsonArray(point.Recent.Reverse().Select(m => (JsonNode)m.DeepClone()).ToArray()),
                });
            }
            return new JsonObject
            {
                ["versione"] = typeof(AgentEngine).Assembly.GetName().Version?.ToString(),
                ["avviato"] = _started.ToString("O"),
                ["vecchioMonitor"] = new JsonArray(_oldMonitors.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray()),
                ["display"] = clients.Count,
                ["messaggiInviati"] = hub.MessagesSent,
                ["casse"] = casse,
                ["collegati"] = new JsonArray(clients.Select(c => (JsonNode)new JsonObject
                {
                    ["nome"] = c.Name,
                    ["dispositivo"] = c.DeviceId,
                    ["cassa"] = c.CashPoint,
                    ["indirizzo"] = c.Remote,
                }).ToArray()),
                ["copieCartacee"] = new JsonArray(_prints.Reverse().Select(p => (JsonNode)p.DeepClone()).ToArray()),
            };
        }
    }

    /// <summary>Gli id dei dispositivi con almeno un display collegato adesso.</summary>
    public HashSet<string> ConnectedDevices() => hub.Clients.Where(c => c.DeviceId != null).Select(c => c.DeviceId!).ToHashSet();

    async Task RunReplay(string folder, CancellationToken stop)
    {
        CashPoint? point;
        int generation;
        lock (_gate)
        {
            point = _points.Find(p => p.Replay);
            generation = point?.Generation ?? 0;
        }
        if (point == null)
        {
            log.LogError("Replay: nessuna cassa nelle impostazioni");
            return;
        }
        try
        {
            await Replay(point, generation, folder, stop);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate)
                point.Problem = $"Replay interrotto: {ex.Message}";
            log.LogError(ex, "Replay interrotto");
        }
    }

    async Task Replay(CashPoint point, int generation, string folder, CancellationToken stop)
    {
        void Problem(string problem)
        {
            lock (_gate)
                point.Problem = problem;
            log.LogError("{Problem}", problem);
        }

        if (!Directory.Exists(folder))
        {
            Problem($"Replay: la cartella {folder} non esiste");
            return;
        }
        var steps = LoadReplay(folder);
        if (steps.Count == 0)
        {
            Problem($"Replay: nessuna versione in {folder}");
            return;
        }
        var speed = Math.Clamp(Options.VelocitaReplay, 0.1, 100);
        log.LogInformation("Replay di {Count} versioni da {Folder} nella cassa {Name} (velocità {Speed})", steps.Count, folder, point.Name, speed);
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
                OnFile(point, generation, await File.ReadAllBytesAsync(file, stop));
            }
            await Task.Delay(TimeSpan.FromSeconds(5 / speed), stop);
            lock (_gate)
            {
                if (point.Generation != generation)
                    return;
                Publish(point, point.Tracker.CloseNow());
            }
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
