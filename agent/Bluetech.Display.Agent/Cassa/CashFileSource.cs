using System.Security.Cryptography;
using System.Threading.Channels;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>
/// Sorveglia Scontrino.txt, lo legge quando Passepartout ha finito di scriverlo e lo toglie subito dopo, così
/// Passepartout scrive il successivo senza aspettare (prova A/B/A del 20/10: 3 s di attesa se il file resta lì).
///
/// Stessa logica del registratore (tools/), provata in cassa:
/// - lettura con condivisione totale; è "stabile" se dimensione e data non cambiano durante la lettura e nei 30 ms
///   dopo, e se nessuno ha più il file aperto in scrittura (su Windows);
/// - il file si toglie solo se contiene ancora la versione letta: lo si apre negando la scrittura, si ricontrolla il
///   contenuto e lo si elimina prima di chiuderlo. Una versione arrivata nel frattempo si rilegge, non si perde;
/// - all'avvio un file più vecchio di <c>staleAfter</c> è un avanzo: si toglie senza mostrarlo.
/// </summary>
public sealed class CashFileSource : IDisposable
{
    const int PollMilliseconds = 250;
    const int ReadAttempts = 200;
    const int ReadRetryDelayMilliseconds = 10;
    const int StabilityRounds = 5;
    const int StabilityDelayMilliseconds = 30;
    const int WriterWaitMilliseconds = 100;
    const int ConsumeAttempts = 25;
    const int ConsumeRetryDelayMilliseconds = 20;
    const int LeftBehindRetryMilliseconds = 2000;
    const int WatcherRetryMilliseconds = 10_000;

    readonly string _path;
    readonly string _folder;
    readonly string _fileName;
    readonly TimeSpan _staleAfter;
    readonly ILogger _log;
    readonly Action<byte[]> _onFile;
    readonly Channel<string> _triggers = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    readonly CancellationTokenSource _stop = new();

    FileSystemWatcher? _watcher;
    Timer? _poll;
    Task? _worker;
    volatile FileMeta _observed = FileMeta.Missing;
    // L'ultima lettura non era stabile (Passepartout stava scrivendo) o il file era bloccato: si riprova al prossimo
    // controllo periodico anche se i metadati non cambiano (la chiusura del file non sempre li cambia).
    volatile bool _retryPending;
    // Un contenuto da togliere senza mostrarlo: una versione già consegnata che non si è riusciti a togliere (si
    // riprova ogni 2 s) o l'avanzo trovato all'avvio. Lo usa solo il worker.
    string? _doNotDeliver;
    FileMeta? _doNotDeliverMeta;
    long _retryAfter;
    // La sorveglianza della cartella si è interrotta: la riattiva il controllo periodico, mai il gestore dell'errore
    // (su una cartella che non la supporta si richiamerebbe da solo all'infinito).
    volatile bool _watcherBroken;
    long _watcherRetryAt;
    int _watcherFailures;
    int _polling;

    public CashFileSource(string path, TimeSpan staleAfter, ILogger log, Action<byte[]> onFile)
    {
        _path = Path.GetFullPath(path);
        _folder = Path.GetDirectoryName(_path)!;
        _fileName = Path.GetFileName(_path);
        _staleAfter = staleAfter;
        _log = log;
        _onFile = onFile;
    }

    public string FilePath => _path;
    public long FilesRead { get; private set; }
    public long FilesRemoved { get; private set; }
    public DateTimeOffset? LastFileAt { get; private set; }
    public string? LastProblem { get; private set; }

    public void Start()
    {
        if (!Directory.Exists(_folder))
        {
            LastProblem = $"La cartella {_folder} non esiste: la creo e aspetto il file.";
            _log.LogWarning("La cartella {Folder} non esiste: la creo e aspetto che Passepartout scriva il file", _folder);
            Directory.CreateDirectory(_folder);
        }

        RemoveStaleFile();

        _watcher = new FileSystemWatcher(_folder, _fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, _) => Trigger("modificato");
        _watcher.Created += (_, _) => Trigger("creato");
        _watcher.Renamed += (_, _) => Trigger("rinominato");
        _watcher.Error += (_, e) => WatcherFailed(e.GetException());

        _worker = Task.Run(WorkerLoop);
        StartWatcher();
        _observed = FileMeta.Read(_path);
        Trigger("avvio");
        _poll = new Timer(_ => Poll(), null, PollMilliseconds, PollMilliseconds);
        _log.LogInformation("Sorveglio {Path}: il file viene tolto dopo ogni lettura", _path);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _triggers.Writer.TryComplete();
        _poll?.Dispose();
        _watcher?.Dispose();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
    }

    void Trigger(string source) => _triggers.Writer.TryWrite(source);

    void StartWatcher()
    {
        try
        {
            _watcher!.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            WatcherFailed(ex);
        }
    }

    void WatcherFailed(Exception? ex)
    {
        _watcherBroken = true;
        _watcherRetryAt = Environment.TickCount64 + WatcherRetryMilliseconds;
        if (Interlocked.Increment(ref _watcherFailures) == 1)
            _log.LogWarning(ex, "Sorveglianza della cartella interrotta: continuo con il controllo ogni {Ms} ms e la riattivo ogni 10 s", PollMilliseconds);
        Trigger("errore-sorveglianza");
    }

    void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return;   // il controllo precedente non ha ancora finito
        try
        {
            var meta = FileMeta.Read(_path);
            var retry = _retryPending && meta.Exists && Environment.TickCount64 >= Interlocked.Read(ref _retryAfter);
            if (!meta.Equals(_observed) || retry)
            {
                _observed = meta;
                Trigger("controllo-periodico");
            }
            if (_watcherBroken && Environment.TickCount64 >= _watcherRetryAt && !_stop.IsCancellationRequested)
            {
                _watcherBroken = false;
                try
                {
                    _watcher!.EnableRaisingEvents = false;
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                }
                StartWatcher();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    async Task WorkerLoop()
    {
        var reader = _triggers.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_stop.Token))
            {
                while (reader.TryRead(out _))
                {
                    // più notifiche insieme = una sola lettura
                }
                try
                {
                    Snapshot();
                }
                catch (Exception ex)
                {
                    LastProblem = ex.Message;
                    _log.LogError(ex, "Errore leggendo {Path}", _path);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void Snapshot()
    {
        var read = ReadStable();
        _observed = read.Meta;
        _retryPending = !read.Missing && !read.Stable;
        Interlocked.Exchange(ref _retryAfter, 0);
        if (read.Missing)
        {
            _doNotDeliver = null;   // tolto da altri: la prossima versione, anche uguale, si mostra
            _doNotDeliverMeta = null;
        }
        if (read.Missing || !read.Stable || read.Bytes == null)
        {
            if (read.Locked)
                LastProblem = "File bloccato da un altro programma per oltre 2 secondi";
            return;
        }

        var hash = Hash(read.Bytes);
        // Lo stesso file rimasto lì non si mostra di nuovo; lo stesso contenuto riscritto da Passepartout sì (la data
        // cambia): dice che la cassa sta lavorando e fa ripartire il tempo di chiusura.
        var deliver = hash != _doNotDeliver || read.Meta != _doNotDeliverMeta;
        if (deliver)
        {
            FilesRead++;
            LastFileAt = DateTimeOffset.Now;
        }
        if (Consume(hash, warn: deliver))
        {
            _doNotDeliver = null;
            _doNotDeliverMeta = null;
        }
        else
        {
            // Rimasto lì: Passepartout aspetterebbe 3 s a ogni battuta (o rinuncerebbe). Si riprova ogni 2 s, senza
            // mostrarlo di nuovo.
            _doNotDeliver = hash;
            _doNotDeliverMeta = read.Meta;
            _retryPending = true;
            Interlocked.Exchange(ref _retryAfter, Environment.TickCount64 + LeftBehindRetryMilliseconds);
        }
        if (deliver)
            _onFile(read.Bytes);
    }

    void RemoveStaleFile()
    {
        if (_staleAfter <= TimeSpan.Zero)
            return;
        try
        {
            var meta = FileMeta.Read(_path);
            if (!meta.Exists)
                return;
            var age = DateTime.UtcNow - meta.LastWriteUtc;
            if (age < _staleAfter)
                return;
            var bytes = ReadShared(out _);
            if (bytes == null)
                return;
            var hash = Hash(bytes);
            if (Consume(hash, warn: true))
            {
                _log.LogInformation("All'avvio ho trovato {File} scritto {Minutes:0} minuti fa: tolto senza mostrarlo", _fileName, age.TotalMinutes);
            }
            else
            {
                _doNotDeliver = hash;   // il worker riprova a toglierlo, ma non lo mostra
                _doNotDeliverMeta = meta;
                _retryPending = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Non riesco a togliere il vecchio {File}", _fileName);
        }
    }

    /// <summary>
    /// Toglie il file solo se contiene ancora la versione letta (<paramref name="readHash"/>). Falso se è rimasto lì
    /// con quel contenuto; vero se è stato tolto, se non c'è più o se nel frattempo è cambiato (e allora si rilegge).
    /// </summary>
    bool Consume(string readHash, bool warn)
    {
        for (var attempt = 0; attempt < ConsumeAttempts; attempt++)
        {
            try
            {
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096))
                {
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    if (Hash(memory.ToArray()) != readHash)
                    {
                        Trigger("contenuto-cambiato");   // nel frattempo è arrivata un'altra versione: si rilegge
                        return true;
                    }
                    File.Delete(_path);
                }
                FilesRemoved++;
                return true;
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(ConsumeRetryDelayMilliseconds);   // aperto proprio ora da Passepartout: si riprova
            }
            catch (UnauthorizedAccessException ex)
            {
                LastProblem = $"Non posso togliere {_fileName}: {ex.Message}";
                if (warn)
                    _log.LogWarning(ex, "Non posso togliere {File}: controlla i permessi della cartella. Riprovo ogni 2 s", _fileName);
                return false;
            }
        }
        LastProblem = $"{_fileName} è rimasto aperto da un altro programma e non è stato tolto";
        if (warn)
            _log.LogWarning("{File} è rimasto aperto da un altro programma e non è stato tolto. Riprovo ogni 2 s", _fileName);
        return false;
    }

    sealed class ReadResult
    {
        public byte[]? Bytes;
        public FileMeta Meta = FileMeta.Missing;
        public bool Missing;
        public bool Locked;
        public bool Stable;
    }

    ReadResult ReadStable()
    {
        var result = new ReadResult();
        for (var round = 0; round < StabilityRounds; round++)
        {
            var before = FileMeta.Read(_path);
            var bytes = ReadShared(out var missing);
            if (missing)
            {
                result.Missing = true;
                result.Meta = FileMeta.Read(_path);
                return result;
            }
            if (bytes == null)
            {
                result.Locked = true;
                result.Meta = FileMeta.Read(_path);
                return result;
            }

            var after = FileMeta.Read(_path);
            result.Bytes = bytes;
            result.Meta = after;
            Thread.Sleep(StabilityDelayMilliseconds);
            if (!before.Equals(after) || !after.Exists || bytes.LongLength != after.Length)
                continue;
            if (!FileMeta.Read(_path).Equals(after))
                return result;
            if (WriterActive())
            {
                Thread.Sleep(WriterWaitMilliseconds);
                continue;
            }
            result.Stable = true;
            return result;
        }
        return result;
    }

    bool WriterActive()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1))
                return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    byte[]? ReadShared(out bool missing)
    {
        missing = false;
        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                missing = true;
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(ReadRetryDelayMilliseconds);
            }
        }
        return null;
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    sealed record FileMeta(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static readonly FileMeta Missing = new(false, 0, default);

        public static FileMeta Read(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileMeta(true, info.Length, info.LastWriteTimeUtc) : Missing;
        }
    }
}
