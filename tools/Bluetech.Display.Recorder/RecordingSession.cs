using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Bluetech.Display.Recorder;

public interface IRecorderLog
{
    void Line(string text, ConsoleColor? color = null);
}

/// <summary>
/// Registra ogni versione del file di scambio così com'è (byte per byte), con un indice eventi.jsonl.
/// Il file viene aperto solo in lettura e con condivisione completa, per non ostacolare Passepartout né il vecchio monitor.
/// </summary>
public sealed class RecordingSession : IDisposable
{
    const int PollMilliseconds = 250;
    const int ReadAttempts = 200;           // ~2 s di tentativi se il file è bloccato
    const int ReadRetryDelayMilliseconds = 10;
    const int StabilityRounds = 5;
    const int StabilityDelayMilliseconds = 30;
    const int WriterWaitMilliseconds = 100;
    const int ConsumeAttempts = 25;
    const int ConsumeRetryDelayMilliseconds = 20;
    const string UnstableFolder = "letture-instabili";

    static readonly string[] ContextFiles = { "XMLMonitorCliente.xml", "MonitorCliente.exe.config" };

    readonly IRecorderLog _log;
    readonly object _sync = new();
    readonly BlockingCollection<string> _triggers = new();
    readonly Stopwatch _clock = new();
    readonly Dictionary<string, int> _seqByHash = new();
    readonly Dictionary<string, int> _versionsByKind = new();
    readonly HashSet<string> _encodings = new();

    FileSystemWatcher _watcher;
    Timer _poll;
    Thread _worker;
    StreamWriter _index;
    // Ultimi metadati visti (da una lettura o dal controllo periodico): il controllo periodico rilegge solo se cambiano.
    volatile FileMeta _observedMeta = FileMeta.Missing;

    int _seq;
    string _lastHash;
    DateTime _lastWriteUtc;
    long _lastVersionMs = -1;
    bool _lastMissing;
    string _lastUnstableHash;
    List<string> _lastReceipt;
    string _lastTotal;

    int _rewrites;
    int _duplicateNotifications;
    int _readRetries;
    int _unstableReads;
    int _notes;
    int _folderEvents;
    int _consumed;
    readonly Dictionary<string, int> _otherFileChanges = new(StringComparer.OrdinalIgnoreCase);

    // Modalità consumo: dopo ogni versione il file viene tolto. Serve a verificare un'ipotesi: che Passepartout non
    // sovrascriva un file esistente (SovrascriviSeEsistente=false) e aspetti che qualcuno lo tolga.
    volatile bool _consume;
    volatile bool _removedByUs;
    // Passepartout ha già sostituito da sé un file che c'era (sovrascrivendolo, o eliminandolo e ricreandolo),
    // senza che lo togliessimo noi: quindi non resta bloccato da un file esistente.
    volatile bool _replacedByWriter;
    // Tempo tra due scritture di Passepartout (versioni e riscritture), il minimo con e senza /consuma: dice se
    // Passepartout aspetta che il file venga tolto prima di scrivere il successivo.
    long _lastWriteMs = -1;
    long _minWriteGapMs = -1;
    long _minWriteGapConsumeMs = -1;
    // Quante cose sono successe al file (versioni, riscritture, letture instabili, sparizioni): serve a capire
    // se tra due note dell'operatore Passepartout ha scritto qualcosa.
    int _activity;
    int _activityAtLastNote = -1;
    int _activityAtConsumeChange;
    IReadOnlyList<string> _oldMonitors = new string[0];
    bool _started;
    bool _stopping;     // Stop() chiamato: niente nuove notifiche e note, ma la coda viene smaltita
    bool _stopped;      // indice chiuso
    readonly ManualResetEventSlim _finished = new(false);

    public RecordingSession(string targetPath, string outputRoot, IRecorderLog log)
    {
        TargetPath = Path.GetFullPath(targetPath);
        Folder = Path.GetDirectoryName(TargetPath);
        FileName = Path.GetFileName(TargetPath);
        OutputRoot = Path.GetFullPath(outputRoot);
        _log = log;
    }

    public string TargetPath { get; }
    public string Folder { get; }
    public string FileName { get; }
    public string OutputRoot { get; }
    public string SessionFolder { get; private set; }
    public string ZipPath { get; private set; }
    public int Versions => _seq;
    public bool IsStopped { get { lock (_sync) return _stopping; } }
    public bool ConsumeMode => _consume;

    /// <summary>Trova il vecchio monitor aperto (sostituibile nei test).</summary>
    public Func<IReadOnlyList<string>> FindOldMonitors { get; set; } = OldMonitor.Running;

    /// <summary>Vecchi monitor aperti all'avvio della sessione (percorsi degli exe).</summary>
    public IReadOnlyList<string> OldMonitors => _oldMonitors;

    /// <summary>
    /// Attiva o disattiva la modalità consumo; se il file c'è già, viene registrato (se nuovo) e tolto subito.
    /// Non si attiva se il vecchio monitor legge lo stesso file, a meno di <paramref name="force"/>: gli toglierebbe il
    /// conto da mostrare. Restituisce false se la modalità non è cambiata.
    /// </summary>
    public bool SetConsumeMode(bool enabled, bool force = false)
    {
        if (enabled && !force)
        {
            var conflict = OldMonitorsOnThisFile(SafeFindOldMonitors());
            if (conflict.Count > 0)
            {
                lock (_sync)
                {
                    if (_stopping || !_started)
                        return false;
                    WriteIndexLocked(new JsonObject { { "tipo", "consumo" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "attivo", false }, { "rifiutato", "vecchio monitor aperto" }, { "vecchioMonitor", conflict } });
                }
                _log.Line($"{Clock()}  Modalità consumo NON attivata: è aperto il vecchio monitor ({string.Join(", ", conflict)}).", ConsoleColor.Magenta);
                _log.Line($"       Legge lo stesso {FileName}: togliendolo dopo ogni lettura, il display del cliente perderebbe gli aggiornamenti", ConsoleColor.Magenta);
                _log.Line("       e il vecchio monitor potrebbe tenerlo aperto proprio quando va tolto. Chiudilo e riscrivi /consuma,", ConsoleColor.Magenta);
                _log.Line("       oppure scrivi /consuma forza.", ConsoleColor.Magenta);
                return false;
            }
        }

        lock (_sync)
        {
            if (_stopping || !_started)
                return false;
            _consume = enabled;
            _activityAtLastNote = -1;   // dopo un cambio di modalità si riparte: niente avviso alla prima nota
            _activityAtConsumeChange = Volatile.Read(ref _activity);
            _lastWriteMs = -1;   // i tempi tra le scritture si misurano dentro ciascuna fase della prova A/B/A
            WriteIndexLocked(new JsonObject { { "tipo", "consumo" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "attivo", enabled }, { "forzato", enabled && force } });
        }
        _log.Line(enabled
            ? $"{Clock()}  Modalità consumo attiva: dopo ogni lettura {FileName} viene tolto (solo se è ancora la versione registrata)."
            : $"{Clock()}  Modalità consumo disattivata: {FileName} non viene più tolto.", ConsoleColor.Cyan);
        if (enabled)
            Trigger("consumo-attivato");
        return true;
    }

    IReadOnlyList<string> SafeFindOldMonitors()
    {
        try
        {
            return FindOldMonitors?.Invoke() ?? new string[0];
        }
        catch (Exception)
        {
            return new string[0];
        }
    }

    /// <summary>Vecchi monitor che leggono il file registrato, o di cui non si conosce la cartella (nel dubbio: sì).</summary>
    List<string> OldMonitorsOnThisFile(IEnumerable<string> monitors) =>
        monitors.Where(exe =>
        {
            var folder = OldMonitor.WatchedFolder(exe);
            return folder == null || SameFolder(folder, Folder);
        }).ToList();

    internal static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public void Start(string machineInfo = null)
    {
        lock (_sync)
        {
            if (_started)
                throw new InvalidOperationException("Sessione già avviata");
            _started = true;
        }
        _oldMonitors = SafeFindOldMonitors();

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        SessionFolder = Path.Combine(OutputRoot, stamp);
        for (var i = 2; Directory.Exists(SessionFolder); i++)
            SessionFolder = Path.Combine(OutputRoot, stamp + "_" + i);
        Directory.CreateDirectory(SessionFolder);

        _index = new StreamWriter(new FileStream(Path.Combine(SessionFolder, "eventi.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };

        File.WriteAllText(Path.Combine(SessionFolder, "sessione.txt"), BuildSessionInfo(machineInfo), new UTF8Encoding(false));
        CopyContextFiles();
        _clock.Start();

        var existing = FileMeta.Read(TargetPath);
        WriteIndex(new JsonObject
        {
            { "tipo", "inizio" },
            { "ora", Now() },
            { "file", TargetPath },
            { "esisteGia", existing.Exists },
            { "attributiFile", existing.Exists ? SafeAttributes(TargetPath) : null },
            { "creazioneFile", existing.Exists ? FileTime(existing.CreationUtc) : null },
            { "ultimaModificaFile", existing.Exists ? FileTime(existing.LastWriteUtc) : null },
            { "vecchioMonitor", _oldMonitors },
        });

        _watcher = new FileSystemWatcher(Folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;

        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Registratore" };
        _worker.Start();

        _triggers.Add("avvio");
        _watcher.EnableRaisingEvents = true;
        _observedMeta = FileMeta.Read(TargetPath);
        _poll = new Timer(_ => Poll(), null, PollMilliseconds, PollMilliseconds);
    }

    /// <summary>Annotazione dell'operatore (es. "ora pago con Satispay"), salvata nell'indice con l'ora.</summary>
    public void AddNote(string text)
    {
        bool stalled;
        lock (_sync)
        {
            if (_stopping || !_started)
                return;
            var activity = Volatile.Read(ref _activity);
            stalled = _activityAtLastNote >= 0 && activity == _activityAtLastNote;
            _activityAtLastNote = activity;
            _notes++;
            WriteIndexLocked(new JsonObject
            {
                { "tipo", "nota" },
                { "ora", Now() },
                { "ms", _clock.ElapsedMilliseconds },
                { "dopoVersione", _seq },
                { "testo", text },
            });
        }
        _log.Line($"{Clock()}  NOTA   {text}", ConsoleColor.Yellow);
        if (stalled)
            WarnNoChanges();
    }

    /// <summary>
    /// Tra due note il file non è cambiato: all'operatore si dice cosa può significare, senza dare per certa una causa.
    /// Il caso finisce anche nell'indice ("caso"), per l'analisi della registrazione.
    /// </summary>
    void WarnNoChanges()
    {
        var meta = FileMeta.Read(TargetPath);
        var since = meta.Exists ? "ultima modifica " + meta.LastWriteUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "il file non c'è";
        var advice = new List<string>();
        string kind;
        var worrying = true;

        if (_consume && meta.Exists)
        {
            kind = "consumo-file-rimasto";
            advice.Add("Il file è ancora lì: non sono riuscito a toglierlo (vedi gli avvisi sopra). Riprovo adesso.");
        }
        else if (_consume && Volatile.Read(ref _activity) != _activityAtConsumeChange)
        {
            kind = "consumo-normale";
            worrying = false;
            advice.Add("Con /consuma attiva Passepartout ha già scritto: è normale se l'operazione non cambia il conto né il visore.");
        }
        else if (_consume)
        {
            kind = "consumo-niente";
            advice.Add("Da quando hai scritto /consuma non è arrivato niente: o le operazioni fatte non mandano nulla al monitor,");
            advice.Add($"o Passepartout scrive in un altro percorso. Controlla il dispositivo monitor in Passepartout: deve essere {TargetPath}.");
        }
        else if (!meta.Exists)
        {
            kind = "file-assente";
            advice.Add($"Controlla il dispositivo monitor in Passepartout: il percorso deve essere {TargetPath}.");
        }
        else if (_replacedByWriter)
        {
            kind = "sovrascrittura-gia-vista";
            worrying = false;
            advice.Add("In questa sessione Passepartout ha già riscritto il file: è normale se l'operazione non cambia il conto né il visore.");
        }
        else
        {
            kind = "mai-riscritto";
            advice.Add("Da quando registro, Passepartout non ha mai riscritto il file. Una causa possibile, da verificare, è che non");
            advice.Add("sovrascriva un file che esiste già. Prova: scrivi /consuma e rifai l'operazione, poi /consuma di nuovo (si spegne)");
            advice.Add("e rifai ancora. Se arriva una versione nuova solo con /consuma attiva, la causa è questa.");
        }

        // Il vecchio monitor è il testimone migliore: legge Scontrino.txt nella cartella del suo exe.
        var monitors = SafeFindOldMonitors();
        var elsewhere = monitors.Select(OldMonitor.WatchedFolder).FirstOrDefault(folder => folder != null && !SameFolder(folder, Folder));
        if (worrying && elsewhere != null)
        {
            advice.Add($"Il vecchio monitor aperto legge {Path.Combine(elsewhere, FileName)}: se il display del cliente si aggiorna, Passepartout scrive lì.");
        }
        else if (worrying && monitors.Count > 0)
        {
            advice.Add("Guarda il display del cliente: se il vecchio monitor si aggiorna e qui non arriva niente, scrivilo in una nota.");
            if (kind == "mai-riscritto")
                advice.Add("Per la prova con /consuma chiudi prima il vecchio monitor: legge lo stesso file.");
        }

        lock (_sync)
        {
            if (!_stopping)
                WriteIndexLocked(new JsonObject { { "tipo", "avviso" }, { "ora", Now() }, { "caso", kind }, { "testo", "Nessuna modifica del file dalla nota precedente" } });
        }
        var color = worrying ? ConsoleColor.Magenta : ConsoleColor.DarkGray;
        _log.Line(worrying
            ? $"       Attenzione: dalla nota precedente {FileName} non è cambiato ({since})."
            : $"       Dalla nota precedente {FileName} non è cambiato ({since}).", color);
        foreach (var line in advice)
            _log.Line("       " + line, color);
        if (kind == "consumo-file-rimasto")
            Trigger("consumo-riprova");
    }

    /// <summary>
    /// Ferma la registrazione, registra le ultime modifiche in coda, scrive il riepilogo e crea lo zip da inviare.
    /// Si può chiamare più volte e da più thread: le chiamate successive aspettano che la prima finisca.
    /// </summary>
    public void Stop()
    {
        bool alreadyStopping;
        lock (_sync)
        {
            if (!_started)
                return;
            alreadyStopping = _stopping;
            _stopping = true;
        }

        if (alreadyStopping)
        {
            _finished.Wait(TimeSpan.FromSeconds(15));
            return;
        }

        try
        {
            StopCore();
        }
        finally
        {
            _finished.Set();
        }
    }

    void StopCore()
    {
        try { _watcher.EnableRaisingEvents = false; } catch { }
        _watcher.Dispose();
        _poll.Dispose();
        _triggers.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(3));

        var summary = BuildSummary();
        lock (_sync)
        {
            _stopped = true;
            WriteIndexLocked(new JsonObject
            {
                { "tipo", "fine" },
                { "ora", Now() },
                { "ms", _clock.ElapsedMilliseconds },
                { "versioni", _seq },
            });
            _index.Dispose();
        }
        File.WriteAllText(Path.Combine(SessionFolder, "riepilogo.txt"), summary, new UTF8Encoding(false));

        _log.Line("");
        foreach (var line in summary.TrimEnd().Split('\n'))
            _log.Line(line.TrimEnd('\r'), ConsoleColor.Cyan);

        try
        {
            // Prima con un nome temporaneo: uno zip interrotto (PC spento) non sembra mai completo.
            var zip = SessionFolder + ".zip";
            var temp = zip + ".tmp";
            if (File.Exists(temp))
                File.Delete(temp);
            ZipFile.CreateFromDirectory(SessionFolder, temp, CompressionLevel.Optimal, includeBaseDirectory: true);
            if (File.Exists(zip))
                File.Delete(zip);
            File.Move(temp, zip);
            ZipPath = zip;
        }
        catch (Exception ex)
        {
            _log.Line("Impossibile creare lo zip: " + ex.Message + " — invia la cartella " + SessionFolder, ConsoleColor.Red);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (IsTarget(e.Name))
        {
            // In modalità consumo creazioni ed eliminazioni sono la norma: restano nell'indice ma non a video.
            if (e.ChangeType != WatcherChangeTypes.Changed && !(e.ChangeType == WatcherChangeTypes.Deleted && _removedByUs))
                LogFolderEvent(EventName(e.ChangeType), e.Name, null, quiet: _consume);
            Trigger(EventName(e.ChangeType));
            return;
        }
        if (IsOwnOutput(e.Name))
            return;

        // Altri file della cartella: creazioni ed eliminazioni sempre, modifiche solo la prima volta per nome
        // (se Passepartout scrive con un altro nome, si vede subito).
        if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted)
        {
            LogFolderEvent(EventName(e.ChangeType), e.Name, null);
        }
        else if (e.ChangeType == WatcherChangeTypes.Changed)
        {
            bool first;
            lock (_sync)
            {
                _otherFileChanges.TryGetValue(e.Name, out var count);
                _otherFileChanges[e.Name] = count + 1;
                first = count == 0;
            }
            if (first)
                LogFolderEvent("modificato", e.Name, null);
        }
    }

    void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (IsOwnOutput(e.Name) || IsOwnOutput(e.OldName))
            return;
        LogFolderEvent("rinominato", e.Name, e.OldName);
        if (IsTarget(e.Name) || IsTarget(e.OldName))
            Trigger("rinominato");
    }

    void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var message = e.GetException()?.Message ?? "errore sconosciuto";
        lock (_sync)
        {
            if (_stopping)
                return;
            WriteIndexLocked(new JsonObject { { "tipo", "errore" }, { "ora", Now() }, { "testo", "Sorveglianza cartella: " + message } });
        }
        _log.Line($"{Clock()}  Avviso: sorveglianza cartella interrotta ({message}), continuo con il controllo periodico", ConsoleColor.DarkYellow);
        Trigger("errore-sorveglianza");
        // Dopo un overflow del buffer il watcher va riattivato.
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    void Poll()
    {
        try
        {
            var meta = FileMeta.Read(TargetPath);
            if (meta.Equals(_observedMeta))
                return;
            _observedMeta = meta;
            Trigger("controllo-periodico");
        }
        catch { }
    }

    void Trigger(string source)
    {
        try
        {
            if (!_triggers.IsAddingCompleted)
                _triggers.Add(source);
        }
        catch (InvalidOperationException) { }   // coda già chiusa: registrazione in chiusura
    }

    void WorkerLoop()
    {
        foreach (var first in _triggers.GetConsumingEnumerable())
        {
            // Più notifiche arrivate insieme = una sola lettura: il contenuto letto è comunque l'ultimo.
            var sources = new List<string> { first };
            while (_triggers.TryTake(out var more))
                if (!sources.Contains(more))
                    sources.Add(more);

            try
            {
                Snapshot(string.Join(",", sources));
            }
            catch (Exception ex)
            {
                _log.Line($"{Clock()}  Errore durante la registrazione: {ex.Message}", ConsoleColor.Red);
                lock (_sync)
                {
                    if (!_stopped)
                        WriteIndexLocked(new JsonObject { { "tipo", "errore" }, { "ora", Now() }, { "testo", ex.ToString() } });
                }
            }
        }
    }

    void Snapshot(string sources)
    {
        var read = ReadStable();
        _observedMeta = read.Meta;
        if (read.Missing)
        {
            if (_lastMissing)
                return;
            _lastMissing = true;
            // Tolto da noi (modalità consumo): non è un evento di Passepartout, e lo stesso conto riscritto
            // va riconosciuto come riscrittura, non come versione nuova.
            if (_removedByUs)
                return;
            _lastHash = null;
            Interlocked.Increment(ref _activity);
            lock (_sync)
            {
                if (_stopped) return;
                WriteIndexLocked(new JsonObject { { "tipo", "assente" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "origine", sources } });
            }
            _log.Line($"{Clock()}  ----   {FileName} non presente", ConsoleColor.DarkGray);
            return;
        }
        if (read.Locked)
        {
            _log.Line($"{Clock()}  Avviso: {FileName} bloccato da un altro programma per oltre 2 secondi", ConsoleColor.DarkYellow);
            lock (_sync)
            {
                if (!_stopped)
                    WriteIndexLocked(new JsonObject { { "tipo", "errore" }, { "ora", Now() }, { "testo", "File bloccato oltre 2 s" }, { "origine", sources } });
            }
            return;
        }

        // C'era già una versione e non l'abbiamo tolta noi: se ora il file è stato riscritto, Passepartout l'ha
        // sostituito da sé (sovrascrivendolo, o eliminandolo e ricreandolo).
        var replacedExisting = _seq > 0 && !_removedByUs;
        _lastMissing = false;
        _readRetries += read.Retries;
        var hash = Sha256(read.Bytes);
        if (!read.Stable)
        {
            // _removedByUs resta com'è: un file appena ricreato e preso ancora vuoto o a metà (Passepartout lo crea
            // vuoto e lo riempie subito dopo) non cambia chi aveva tolto il precedente.
            Interlocked.Increment(ref _activity);
            RecordUnstable(read, hash, sources);
            return;
        }
        _removedByUs = false;
        // Il file trovato all'avvio può essere stato scritto molto prima: non è una scrittura da cronometrare.
        var timed = !sources.Split(',').Contains("avvio");

        if (hash == _lastHash)
        {
            // Stesso contenuto: è una vera riscrittura solo se la data di modifica (letta dopo il contenuto) è cambiata.
            if (read.Meta.LastWriteUtc != _lastWriteUtc)
            {
                if (replacedExisting)
                    _replacedByWriter = true;
                if (timed)
                    NoteWriteGap();
                _lastWriteUtc = read.Meta.LastWriteUtc;
                _rewrites++;
                Interlocked.Increment(ref _activity);
                lock (_sync)
                {
                    if (_stopped) return;
                    WriteIndexLocked(new JsonObject
                    {
                        { "tipo", "riscrittura" },
                        { "ora", Now() },
                        { "ms", _clock.ElapsedMilliseconds },
                        { "uguale", _seq },
                        { "msDallaVersione", _clock.ElapsedMilliseconds - _lastVersionMs },
                        { "ultimaModificaFile", FileTime(read.Meta.LastWriteUtc) },
                        { "creazioneFile", FileTime(read.Meta.CreationUtc) },
                        { "origine", sources },
                    });
                }
                _log.Line($"{Clock()}  ·      riscritto identico a #{_seq:0000}", ConsoleColor.DarkGray);
            }
            else
            {
                _duplicateNotifications++;
            }
            ConsumeIfEnabled(hash);
            return;
        }

        if (replacedExisting)
            _replacedByWriter = true;
        if (RecordVersion(read.Bytes, hash, read.Meta, read.Retries, read.WriterWaitMs, sources))
        {
            if (timed)
                NoteWriteGap();
            ConsumeIfEnabled(hash);
        }
    }

    void NoteWriteGap()
    {
        var now = _clock.ElapsedMilliseconds;
        if (_lastWriteMs >= 0)
        {
            var gap = now - _lastWriteMs;
            if (_consume)
                _minWriteGapConsumeMs = _minWriteGapConsumeMs < 0 ? gap : Math.Min(_minWriteGapConsumeMs, gap);
            else
                _minWriteGapMs = _minWriteGapMs < 0 ? gap : Math.Min(_minWriteGapMs, gap);
        }
        _lastWriteMs = now;
    }

    /// <summary>
    /// Modalità consumo: toglie il file appena registrato, come farebbe un lettore che "consuma" i file di scambio.
    /// Lo toglie solo se contiene ancora la versione registrata (<paramref name="recordedHash"/>): lo apre negando la
    /// scrittura (se Passepartout vuole scrivere proprio ora, trova il file occupato e ritenta come fa di suo),
    /// ricontrolla il contenuto e lo elimina prima di chiuderlo. Una versione arrivata nel frattempo non viene tolta:
    /// si rilegge, si registra e solo dopo si toglie.
    /// Resta un limite: il file è eliminato per nome, e l'handle lascia eliminare. Se Passepartout eliminasse e
    /// ricreasse il file proprio tra il controllo e l'eliminazione (frazioni di millisecondo, contro scritture ad
    /// almeno 3 s l'una dall'altra) si toglierebbe il nuovo.
    /// </summary>
    void ConsumeIfEnabled(string recordedHash)
    {
        if (!_consume)
            return;
        for (var attempt = 0; attempt < ConsumeAttempts; attempt++)
        {
            try
            {
                using (var stream = new FileStream(TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096))
                {
                    if (Sha256(ReadToEnd(stream)) != recordedHash)
                    {
                        lock (_sync)
                        {
                            if (!_stopped)
                                WriteIndexLocked(new JsonObject { { "tipo", "consumoRinviato" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "dopoVersione", _seq }, { "motivo", "contenuto cambiato dopo la lettura" } });
                        }
                        Trigger("consumo-verifica");
                        return;
                    }
                    _removedByUs = true;
                    File.Delete(TargetPath);
                }
                _consumed++;
                lock (_sync)
                {
                    if (!_stopped)
                        WriteIndexLocked(new JsonObject { { "tipo", "consumato" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "dopoVersione", _seq } });
                }
                _log.Line("         file tolto: Passepartout può scrivere il prossimo", ConsoleColor.DarkGray);
                return;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return;   // tolto da qualcun altro: lo registrerà la prossima lettura
            }
            catch (IOException)
            {
                // Aperto in scrittura proprio ora (Passepartout), o in lettura dal vecchio monitor: si riprova tra un attimo.
                _removedByUs = false;
                Thread.Sleep(ConsumeRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException ex)
            {
                _removedByUs = false;
                _log.Line($"{Clock()}  Avviso: impossibile togliere {FileName} ({ex.Message}). Avvia il registratore con un utente che può modificare la cartella.", ConsoleColor.DarkYellow);
                return;
            }
        }
        _log.Line($"{Clock()}  Avviso: {FileName} è rimasto aperto da un altro programma, non è stato tolto", ConsoleColor.DarkYellow);
    }

    static byte[] ReadToEnd(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// Contenuto letto mentre il file cambiava (troncato, parziale o subito sostituito): non è uno stato che il monitor
    /// avrebbe mostrato, quindi non diventa una versione. Si conserva a parte perché racconta come scrive Passepartout.
    /// </summary>
    void RecordUnstable(ReadResult read, string hash, string sources)
    {
        if (hash == _lastUnstableHash)
            return;
        _lastUnstableHash = hash;

        var name = $"dopo{_seq:0000}_{DateTime.Now.ToString("HH-mm-ss.fff", CultureInfo.InvariantCulture)}.txt";
        var folder = Path.Combine(SessionFolder, UnstableFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), read.Bytes);

        lock (_sync)
        {
            if (_stopped) return;
            _unstableReads++;
            WriteIndexLocked(new JsonObject
            {
                { "tipo", "letturaInstabile" },
                { "ora", Now() },
                { "ms", _clock.ElapsedMilliseconds },
                { "dopoVersione", _seq },
                { "file", UnstableFolder + "/" + name },
                { "byte", read.Bytes.Length },
                { "byteMetadati", read.Meta.Length },
                { "ultimaModificaFile", FileTime(read.Meta.LastWriteUtc) },
                { "attesaScritturaMs", read.WriterWaitMs },
                { "origine", sources },
            });
        }
        _log.Line($"{Clock()}  ·      lettura durante la scrittura ({read.Bytes.Length} byte), messa da parte", ConsoleColor.DarkGray);
    }

    sealed class ReadResult
    {
        public byte[] Bytes;
        public FileMeta Meta = FileMeta.Missing;   // metadati letti dopo il contenuto
        public bool Missing;
        public bool Locked;
        public bool Stable;
        public int Retries;
        public int WriterWaitMs;                   // attesa perché Passepartout aveva ancora il file aperto in scrittura
    }

    /// <summary>
    /// Lettura che non impedisce a nessuno di scrivere. È "stabile" se:
    /// - dimensione e data del file non cambiano durante la lettura né nei 30 ms successivi, e i byte letti coincidono
    ///   con la dimensione;
    /// - su Windows, nessuno ha più il file aperto in scrittura (è la condizione in cui il vecchio monitor riesce a leggerlo).
    /// Così un file preso a metà scrittura (troncato o incompleto) non viene scambiato per un conto vero.
    /// </summary>
    ReadResult ReadStable()
    {
        var result = new ReadResult();
        for (var round = 0; round < StabilityRounds; round++)
        {
            var before = FileMeta.Read(TargetPath);
            var bytes = ReadShared(out var retries, out var missing);
            result.Retries += retries;
            if (missing)
            {
                result.Missing = true;
                result.Meta = FileMeta.Read(TargetPath);
                return result;
            }
            if (bytes == null)
            {
                result.Locked = true;
                result.Meta = FileMeta.Read(TargetPath);
                return result;
            }

            var after = FileMeta.Read(TargetPath);
            result.Bytes = bytes;
            result.Meta = after;
            Thread.Sleep(StabilityDelayMilliseconds);
            if (!before.Equals(after) || !after.Exists || bytes.LongLength != after.Length)
                continue;   // il file è cambiato mentre lo leggevamo: si rilegge
            if (!FileMeta.Read(TargetPath).Equals(after))
                return result;   // contenuto già superato: la prossima notifica registrerà il nuovo
            if (WriterActive())
            {
                result.WriterWaitMs += StabilityDelayMilliseconds + WriterWaitMilliseconds;
                Thread.Sleep(WriterWaitMilliseconds);
                continue;
            }
            result.Stable = true;
            return result;
        }
        return result;
    }

    /// <summary>
    /// Su Windows: qualcuno ha il file aperto in scrittura? Lo si scopre aprendolo per un istante negando la scrittura,
    /// come fa il vecchio monitor a ogni lettura (lui però lo tiene aperto per tutta la lettura).
    /// </summary>
    bool WriterActive()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return false;
        try
        {
            using (new FileStream(TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1))
                return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;   // violazione di condivisione: il file è ancora aperto in scrittura
        }
    }

    /// <summary>Registra una versione nuova; false se la sessione è già chiusa.</summary>
    bool RecordVersion(byte[] bytes, string hash, FileMeta meta, int retries, int writerWaitMs, string sources)
    {
        var elapsed = _clock.ElapsedMilliseconds;
        var seq = _seq + 1;
        var time = DateTime.Now;
        var fileName = $"{seq:0000}_{time.ToString("HH-mm-ss.fff", CultureInfo.InvariantCulture)}.txt";
        File.WriteAllBytes(Path.Combine(SessionFolder, fileName), bytes);

        var analysis = FileAnalyzer.Analyze(bytes);
        var entry = new JsonObject
        {
            { "tipo", "versione" },
            { "seq", seq },
            { "ora", Now() },
            { "ms", elapsed },
            { "msDalPrecedente", _lastVersionMs < 0 ? null : (object)(elapsed - _lastVersionMs) },
            { "file", fileName },
            { "byte", bytes.Length },
            { "byteMetadati", meta.Length },
            { "ultimaModificaFile", FileTime(meta.LastWriteUtc) },
            { "creazioneFile", FileTime(meta.CreationUtc) },
            { "sha256", hash.Substring(0, 16) },
            { "uguale", _seqByHash.TryGetValue(hash, out var same) ? (object)same : null },
            { "codifica", analysis.Encoding },
            { "fineRiga", analysis.LineEndings },
            { "righeFile", analysis.Lines.Length },
            { "contenuto", KindName(analysis.Kind) },
            { "origine", sources },
            { "tentativiLettura", retries },
            { "attesaScritturaMs", writerWaitMs },
        };

        var console = new List<OutputLine>();
        var header = $"{Clock(time)}  #{seq:0000}  ";
        switch (analysis.Kind)
        {
            case FileKind.Scontrino:
                DescribeReceipt(analysis, entry, console, ref header);
                break;
            case FileKind.QrCode:
                DescribeQr(analysis, entry, seq, console, ref header);
                break;
            case FileKind.Messaggio:
                entry.Add("testo", analysis.DisplayText);
                if (analysis.HeaderNotExact)
                    entry.Add("avviso", "prima riga SNF con spazi: il vecchio monitor la tratta come messaggio");
                header += $"MESSAGGIO  \"{analysis.DisplayText}\"";
                break;
            default:
                header += "VUOTO (il vecchio monitor lo ignora)";
                break;
        }

        if (same > 0)
            header += $"   (uguale a #{same:0000})";

        lock (_sync)
        {
            if (_stopped)
                return false;
            _seq = seq;
            WriteIndexLocked(entry);
        }
        Interlocked.Increment(ref _activity);

        _seqByHash[hash] = seq;
        _lastHash = hash;
        _lastWriteUtc = meta.LastWriteUtc;
        _lastVersionMs = elapsed;
        var newEncoding = _encodings.Add(analysis.Encoding);
        var kind = KindName(analysis.Kind);
        _versionsByKind[kind] = _versionsByKind.TryGetValue(kind, out var count) ? count + 1 : 1;

        _log.Line(header, analysis.Kind == FileKind.Vuoto ? ConsoleColor.DarkGray : ConsoleColor.White);
        foreach (var line in console)
            _log.Line(line.Text, line.Color);
        if (newEncoding)
            _log.Line($"         codifica {analysis.Encoding}, fine riga {analysis.LineEndings}", ConsoleColor.DarkGray);
        return true;
    }

    void DescribeReceipt(FileAnalysis analysis, JsonObject entry, List<OutputLine> console, ref string header)
    {
        var lines = analysis.ReceiptLines;
        entry.Add("righe", lines.Count);
        entry.Add("subtotale", analysis.Subtotal);
        entry.Add("totale", analysis.Total);

        header += $"SCONTRINO  {lines.Count} {(lines.Count == 1 ? "riga" : "righe")}";
        if (analysis.Total != null)
            header += _lastTotal != null && _lastTotal != analysis.Total
                ? $" · totale {_lastTotal} → {analysis.Total}"
                : $" · totale {analysis.Total}";

        if (_lastReceipt == null)
        {
            foreach (var line in lines)
                console.Add(new OutputLine("         " + line, ConsoleColor.Gray));
        }
        else
        {
            var diff = ReceiptDiff.Compute(_lastReceipt, lines, FileAnalyzer.LineKey);
            var added = diff.Where(d => d.Op == DiffOp.Added).ToList();
            var removed = diff.Where(d => d.Op == DiffOp.Removed).ToList();
            entry.Add("aggiunte", added.Select(d => new JsonObject { { "riga", d.Index + 1 }, { "testo", d.Text.Trim() } }).ToList());
            entry.Add("rimosse", removed.Select(d => new JsonObject { { "riga", d.Index + 1 }, { "testo", d.Text.Trim() } }).ToList());

            // Solo un dato: come si riconosce un conto nuovo lo diranno le registrazioni.
            var common = diff.Count(d => d.Op == DiffOp.Same);
            entry.Add("righeInComune", common);
            if (common == 0 && _lastReceipt.Count > 0 && lines.Count > 0)
                header += "  (nessuna riga in comune con il precedente)";

            foreach (var d in removed)
                console.Add(new OutputLine($"   − {d.Index + 1,3} {d.Text}", ConsoleColor.Red));
            foreach (var d in added)
                console.Add(new OutputLine($"   + {d.Index + 1,3} {d.Text}", ConsoleColor.Green));
            if (added.Count == 0 && removed.Count == 0)
                console.Add(new OutputLine("     righe invariate", ConsoleColor.DarkGray));
        }

        _lastReceipt = lines;
        _lastTotal = analysis.Total;
    }

    void DescribeQr(FileAnalysis analysis, JsonObject entry, int seq, List<OutputLine> console, ref string header)
    {
        var qr = analysis.Qr;
        var info = new JsonObject
        {
            { "base64", qr.IsBase64 },
            { "lunghezza", qr.PayloadLength },
            { "righeDopoQRCODE", qr.LinesAfter },
        };
        header += "QR CODE    ";

        if (qr.IsBase64)
        {
            var imageName = $"{seq:0000}_qrcode.{qr.Format}";
            File.WriteAllBytes(Path.Combine(SessionFolder, imageName), qr.Data);
            info.Add("formato", qr.Format);
            info.Add("larghezza", qr.Width);
            info.Add("altezza", qr.Height);
            info.Add("immagine", imageName);
            header += qr.Format == "bin"
                ? $"Base64 ma non è un'immagine nota ({qr.Data.Length} byte) → {imageName}"
                : $"immagine {qr.Format.ToUpperInvariant()} {(qr.Width > 0 ? $"{qr.Width}×{qr.Height} " : "")}→ {imageName}";
        }
        else if (qr.PayloadLength > 0)
        {
            info.Add("testo", qr.TextPreview);
            header += $"testo (non Base64): \"{qr.TextPreview}\"";
        }
        else
        {
            header += "senza contenuto dopo QRCODE";
        }

        if (qr.LinesAfter > 1)
            console.Add(new OutputLine($"         {qr.LinesAfter - 1} righe dopo il codice (il vecchio monitor le ignora)", ConsoleColor.DarkGray));
        entry.Add("qr", info);
    }

    /// <summary>Lettura con condivisione totale (lettura, scrittura, eliminazione) e ritentativi se il file è bloccato.</summary>
    byte[] ReadShared(out int retries, out bool missing)
    {
        retries = 0;
        missing = false;
        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(TargetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (FileNotFoundException)
            {
                missing = true;
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                missing = true;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                retries++;
                Thread.Sleep(ReadRetryDelayMilliseconds);
            }
        }
        return null;
    }

    void LogFolderEvent(string kind, string name, string oldName, bool quiet = false)
    {
        lock (_sync)
        {
            if (_stopping)
                return;
            _folderEvents++;
            WriteIndexLocked(new JsonObject
            {
                { "tipo", "cartella" },
                { "ora", Now() },
                { "ms", _clock.ElapsedMilliseconds },
                { "evento", kind },
                { "nome", name },
                { "vecchioNome", oldName },
            });
        }
        if (!quiet)
            _log.Line($"{Clock()}  cartella: {kind} {(oldName != null ? oldName + " → " : "")}{name}", ConsoleColor.DarkGray);
    }

    void WriteIndex(JsonObject entry)
    {
        lock (_sync)
            WriteIndexLocked(entry);
    }

    void WriteIndexLocked(JsonObject entry) => _index.WriteLine(entry.ToString());

    bool IsTarget(string name) => string.Equals(name, FileName, StringComparison.OrdinalIgnoreCase);

    bool IsOwnOutput(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        var full = Path.GetFullPath(Path.Combine(Folder, name)).TrimEnd(Path.DirectorySeparatorChar);
        var root = OutputRoot.TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Configurazione del vecchio monitor: dalla cartella registrata e da quella del vecchio monitor aperto.</summary>
    void CopyContextFiles()
    {
        var folders = new List<string> { Folder };
        foreach (var folder in _oldMonitors.Select(OldMonitor.WatchedFolder))
            if (folder != null && !folders.Any(f => SameFolder(f, folder)))
                folders.Add(folder);

        var copied = false;
        foreach (var folder in folders)
        {
            var prefix = folder == Folder ? "" : "vecchio-monitor_";
            foreach (var name in ContextFiles)
            {
                var source = Path.Combine(folder, name);
                try
                {
                    if (!File.Exists(source) || new FileInfo(source).Length > 1024 * 1024)
                        continue;
                    var target = Path.Combine(SessionFolder, "contesto");
                    Directory.CreateDirectory(target);
                    File.Copy(source, Path.Combine(target, prefix + name));
                    copied = true;
                }
                catch { }
            }
        }
        if (copied)
            _log.Line("Copiata la configurazione del vecchio monitor nella cartella \"contesto\".", ConsoleColor.DarkGray);
    }

    string BuildSessionInfo(string machineInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bluetech Display Recorder " + typeof(RecordingSession).Assembly.GetName().Version);
        sb.AppendLine("Inizio: " + Timestamps.Iso(DateTimeOffset.Now));
        sb.AppendLine("File registrato: " + TargetPath);
        sb.AppendLine("Vecchio monitor aperto: " + (_oldMonitors.Count == 0 ? "no" : string.Join(", ", _oldMonitors)));
        if (!string.IsNullOrEmpty(machineInfo))
        {
            sb.AppendLine();
            sb.Append(machineInfo);
        }
        return sb.ToString();
    }

    string BuildSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Riepilogo registrazione");
        sb.AppendLine($"  Durata: {TimeSpan.FromMilliseconds(_clock.ElapsedMilliseconds):hh\\:mm\\:ss}");
        sb.AppendLine($"  Versioni diverse registrate: {_seq}");
        foreach (var pair in _versionsByKind.OrderBy(p => p.Key))
            sb.AppendLine($"    {pair.Key}: {pair.Value}");
        sb.AppendLine($"  Riscritture identiche: {_rewrites}");
        sb.AppendLine($"  Notifiche doppie ignorate: {_duplicateNotifications}");
        sb.AppendLine($"  Letture prese durante la scrittura (messe da parte): {_unstableReads}");
        sb.AppendLine($"  Letture ritentate (file bloccato): {_readRetries}");
        sb.AppendLine($"  Eventi della cartella: {_folderEvents}");
        sb.AppendLine($"  File tolti in modalità consumo: {_consumed}");
        sb.AppendLine($"  Passepartout ha sostituito da sé il file che c'era: {(_replacedByWriter ? "sì" : "no")}");
        sb.AppendLine($"  Tempo minimo tra due scritture: {Seconds(_minWriteGapMs)} (con /consuma: {Seconds(_minWriteGapConsumeMs)})");
        lock (_sync)
        {
            foreach (var pair in _otherFileChanges.OrderBy(p => p.Key))
                sb.AppendLine($"  Altro file modificato nella cartella: {pair.Key} ({pair.Value} {(pair.Value == 1 ? "notifica" : "notifiche")})");
        }
        sb.AppendLine($"  Note: {_notes}");
        sb.AppendLine($"  Codifiche viste: {(_encodings.Count == 0 ? "-" : string.Join(", ", _encodings))}");
        return sb.ToString();
    }

    /// <summary>Attributi del file (es. "ReadOnly, Archive"): un file in sola lettura Passepartout non lo può sostituire.</summary>
    internal static string SafeAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path).ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string Seconds(long ms) =>
        ms < 0 ? "-" : (ms / 1000.0).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " s";

    static string KindName(FileKind kind) => kind switch
    {
        FileKind.Scontrino => "scontrino",
        FileKind.QrCode => "qrcode",
        FileKind.Messaggio => "messaggio",
        _ => "vuoto",
    };

    static string EventName(WatcherChangeTypes type) => type switch
    {
        WatcherChangeTypes.Created => "creato",
        WatcherChangeTypes.Deleted => "eliminato",
        WatcherChangeTypes.Renamed => "rinominato",
        _ => "modificato",
    };

    static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
    }

    static string Now() => Timestamps.Iso(DateTimeOffset.Now);

    static string FileTime(DateTime utc) => utc == default ? null : Timestamps.Iso(new DateTimeOffset(utc, TimeSpan.Zero).ToLocalTime());

    static string Clock(DateTime? time = null) => (time ?? DateTime.Now).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    sealed class OutputLine
    {
        public OutputLine(string text, ConsoleColor? color)
        {
            Text = text;
            Color = color;
        }

        public string Text { get; }
        public ConsoleColor? Color { get; }
    }

    sealed class FileMeta : IEquatable<FileMeta>
    {
        public static readonly FileMeta Missing = new(false, 0, default, default);

        FileMeta(bool exists, long length, DateTime lastWriteUtc, DateTime creationUtc)
        {
            Exists = exists;
            Length = length;
            LastWriteUtc = lastWriteUtc;
            CreationUtc = creationUtc;
        }

        public bool Exists { get; }
        public long Length { get; }
        public DateTime LastWriteUtc { get; }

        /// <summary>
        /// Solo un dato per l'analisi, escluso dal confronto. Attenzione: Windows (tunneling di NTFS) ridà a un file
        /// ricreato con lo stesso nome entro 15 s la data di creazione di quello eliminato.
        /// </summary>
        public DateTime CreationUtc { get; }

        /// <summary>Solo metadati (nessun handle aperto sul file).</summary>
        public static FileMeta Read(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileMeta(true, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc) : Missing;
        }

        public bool Equals(FileMeta other) =>
            other != null && Exists == other.Exists && Length == other.Length && LastWriteUtc == other.LastWriteUtc;

        public override bool Equals(object obj) => Equals(obj as FileMeta);
        public override int GetHashCode() => LastWriteUtc.GetHashCode() ^ Length.GetHashCode();
    }
}

/// <summary>Date ISO 8601 indipendenti dalle impostazioni internazionali del PC (":" è sempre ":").</summary>
public static class Timestamps
{
    public static string Iso(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd'T'HH':'mm':'ss'.'fffzzz", CultureInfo.InvariantCulture);

    /// <summary>Quanto tempo fa, a parole: "40 s", "12 min", "1 ora", "5 ore", "3 giorni".</summary>
    public static string Age(TimeSpan age)
    {
        if (age.TotalSeconds < 90)
            return $"{Math.Max(0, (int)age.TotalSeconds)} s";
        if (age.TotalMinutes < 90)
            return $"{(int)age.TotalMinutes} min";
        if (age.TotalHours < 48)
            return (int)age.TotalHours == 1 ? "1 ora" : $"{(int)age.TotalHours} ore";
        return $"{(int)age.TotalDays} giorni";
    }
}
