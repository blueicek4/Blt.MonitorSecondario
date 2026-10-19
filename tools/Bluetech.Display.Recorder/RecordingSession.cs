using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    FileMeta _pollMeta;

    int _seq;
    string _lastHash;
    DateTime _lastWriteUtc;
    long _lastVersionMs = -1;
    bool _lastMissing;
    List<string> _lastReceipt;
    string _lastTotal;

    int _rewrites;
    int _duplicateNotifications;
    int _readRetries;
    int _notes;
    int _folderEvents;
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

    public void Start(string machineInfo = null)
    {
        lock (_sync)
        {
            if (_started)
                throw new InvalidOperationException("Sessione già avviata");
            _started = true;
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        SessionFolder = Path.Combine(OutputRoot, stamp);
        for (var i = 2; Directory.Exists(SessionFolder); i++)
            SessionFolder = Path.Combine(OutputRoot, stamp + "_" + i);
        Directory.CreateDirectory(SessionFolder);

        _index = new StreamWriter(new FileStream(Path.Combine(SessionFolder, "eventi.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false)) { AutoFlush = true };

        File.WriteAllText(Path.Combine(SessionFolder, "sessione.txt"), BuildSessionInfo(machineInfo), new UTF8Encoding(false));
        CopyContextFiles();
        _clock.Start();

        WriteIndex(new JsonObject
        {
            { "tipo", "inizio" },
            { "ora", Now() },
            { "file", TargetPath },
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
        _pollMeta = FileMeta.Read(TargetPath);
        _poll = new Timer(_ => Poll(), null, PollMilliseconds, PollMilliseconds);
    }

    /// <summary>Annotazione dell'operatore (es. "ora pago con Satispay"), salvata nell'indice con l'ora.</summary>
    public void AddNote(string text)
    {
        lock (_sync)
        {
            if (_stopping || !_started)
                return;
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
            var zip = SessionFolder + ".zip";
            if (File.Exists(zip))
                File.Delete(zip);
            ZipFile.CreateFromDirectory(SessionFolder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
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
            if (e.ChangeType != WatcherChangeTypes.Changed)
                LogFolderEvent(EventName(e.ChangeType), e.Name, null);
            Trigger(EventName(e.ChangeType));
            return;
        }

        // Altri file della cartella: interessano solo creazioni ed eliminazioni (es. file temporanei di Passepartout).
        if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted && !IsOwnOutput(e.Name))
            LogFolderEvent(EventName(e.ChangeType), e.Name, null);
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
            if (meta.Equals(_pollMeta))
                return;
            _pollMeta = meta;
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
        var meta = FileMeta.Read(TargetPath);
        var bytes = ReadShared(out var retries, out var missing);
        if (missing)
        {
            if (_lastMissing)
                return;
            _lastMissing = true;
            _lastHash = null;
            lock (_sync)
            {
                if (_stopped) return;
                WriteIndexLocked(new JsonObject { { "tipo", "assente" }, { "ora", Now() }, { "ms", _clock.ElapsedMilliseconds }, { "origine", sources } });
            }
            _log.Line($"{Clock()}  ----   {FileName} non presente", ConsoleColor.DarkGray);
            return;
        }
        if (bytes == null)
        {
            _log.Line($"{Clock()}  Avviso: {FileName} bloccato da un altro programma per oltre 2 secondi", ConsoleColor.DarkYellow);
            lock (_sync)
            {
                if (!_stopped)
                    WriteIndexLocked(new JsonObject { { "tipo", "errore" }, { "ora", Now() }, { "testo", "File bloccato oltre 2 s" }, { "origine", sources } });
            }
            return;
        }

        _lastMissing = false;
        var hash = Sha256(bytes);
        if (hash == _lastHash)
        {
            // Stesso contenuto: è una vera riscrittura solo se la data di modifica è cambiata.
            if (meta.LastWriteUtc != _lastWriteUtc)
            {
                _lastWriteUtc = meta.LastWriteUtc;
                _rewrites++;
                lock (_sync)
                {
                    if (_stopped) return;
                    WriteIndexLocked(new JsonObject
                    {
                        { "tipo", "riscrittura" },
                        { "ora", Now() },
                        { "ms", _clock.ElapsedMilliseconds },
                        { "uguale", _seq },
                        { "origine", sources },
                    });
                }
                _log.Line($"{Clock()}  ·      riscritto identico a #{_seq:0000}", ConsoleColor.DarkGray);
            }
            else
            {
                _duplicateNotifications++;
            }
            return;
        }

        RecordVersion(bytes, hash, meta, retries, sources);
    }

    void RecordVersion(byte[] bytes, string hash, FileMeta meta, int retries, string sources)
    {
        var elapsed = _clock.ElapsedMilliseconds;
        var seq = _seq + 1;
        var time = DateTime.Now;
        var fileName = $"{seq:0000}_{time:HH-mm-ss.fff}.txt";
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
            { "sha256", hash.Substring(0, 16) },
            { "uguale", _seqByHash.TryGetValue(hash, out var same) ? (object)same : null },
            { "codifica", analysis.Encoding },
            { "fineRiga", analysis.LineEndings },
            { "righeFile", analysis.Lines.Length },
            { "contenuto", KindName(analysis.Kind) },
            { "origine", sources },
            { "tentativiLettura", retries },
        };

        var console = new List<(string Text, ConsoleColor? Color)>();
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
                return;
            _seq = seq;
            WriteIndexLocked(entry);
        }

        _seqByHash[hash] = seq;
        _lastHash = hash;
        _lastWriteUtc = meta.LastWriteUtc;
        _lastVersionMs = elapsed;
        _readRetries += retries;
        var newEncoding = _encodings.Add(analysis.Encoding);
        var kind = KindName(analysis.Kind);
        _versionsByKind[kind] = _versionsByKind.TryGetValue(kind, out var count) ? count + 1 : 1;

        _log.Line(header, analysis.Kind == FileKind.Vuoto ? ConsoleColor.DarkGray : ConsoleColor.White);
        foreach (var (text, color) in console)
            _log.Line(text, color);
        if (newEncoding)
            _log.Line($"         codifica {analysis.Encoding}, fine riga {analysis.LineEndings}", ConsoleColor.DarkGray);
    }

    void DescribeReceipt(FileAnalysis analysis, JsonObject entry, List<(string, ConsoleColor?)> console, ref string header)
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
                console.Add(("         " + line, ConsoleColor.Gray));
        }
        else
        {
            var diff = ReceiptDiff.Compute(_lastReceipt, lines, FileAnalyzer.LineKey);
            var added = diff.Where(d => d.Op == DiffOp.Added).ToList();
            var removed = diff.Where(d => d.Op == DiffOp.Removed).ToList();
            entry.Add("aggiunte", added.Select(d => new JsonObject { { "riga", d.Index + 1 }, { "testo", d.Text.Trim() } }).ToList());
            entry.Add("rimosse", removed.Select(d => new JsonObject { { "riga", d.Index + 1 }, { "testo", d.Text.Trim() } }).ToList());

            var newBill = _lastReceipt.Count > 0 && lines.Count > 0 && diff.All(d => d.Op != DiffOp.Same);
            entry.Add("sembraContoNuovo", newBill);
            if (newBill)
                header += "  ← sembra un conto nuovo";

            foreach (var d in removed)
                console.Add(($"   − {d.Index + 1,3} {d.Text}", ConsoleColor.Red));
            foreach (var d in added)
                console.Add(($"   + {d.Index + 1,3} {d.Text}", ConsoleColor.Green));
            if (added.Count == 0 && removed.Count == 0)
                console.Add(("     righe invariate", ConsoleColor.DarkGray));
        }

        _lastReceipt = lines;
        _lastTotal = analysis.Total;
    }

    void DescribeQr(FileAnalysis analysis, JsonObject entry, int seq, List<(string, ConsoleColor?)> console, ref string header)
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
            console.Add(($"         {qr.LinesAfter - 1} righe dopo il codice (il vecchio monitor le ignora)", ConsoleColor.DarkGray));
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

    void LogFolderEvent(string kind, string name, string oldName)
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

    void CopyContextFiles()
    {
        var copied = false;
        foreach (var name in ContextFiles)
        {
            var source = Path.Combine(Folder, name);
            try
            {
                if (!File.Exists(source) || new FileInfo(source).Length > 1024 * 1024)
                    continue;
                var target = Path.Combine(SessionFolder, "contesto");
                Directory.CreateDirectory(target);
                File.Copy(source, Path.Combine(target, name));
                copied = true;
            }
            catch { }
        }
        if (copied)
            _log.Line("Copiata la configurazione del vecchio monitor nella cartella \"contesto\".", ConsoleColor.DarkGray);
    }

    string BuildSessionInfo(string machineInfo)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bluetech Display Recorder " + typeof(RecordingSession).Assembly.GetName().Version);
        sb.AppendLine("Inizio: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("File registrato: " + TargetPath);
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
        sb.AppendLine($"  Letture ritentate (file bloccato): {_readRetries}");
        sb.AppendLine($"  Eventi della cartella: {_folderEvents}");
        sb.AppendLine($"  Note: {_notes}");
        sb.AppendLine($"  Codifiche viste: {(_encodings.Count == 0 ? "-" : string.Join(", ", _encodings))}");
        return sb.ToString();
    }

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

    static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");

    static string Clock(DateTime? time = null) => (time ?? DateTime.Now).ToString("HH:mm:ss.fff");

    readonly struct FileMeta : IEquatable<FileMeta>
    {
        FileMeta(bool exists, long length, DateTime lastWriteUtc)
        {
            Exists = exists;
            Length = length;
            LastWriteUtc = lastWriteUtc;
        }

        public bool Exists { get; }
        public long Length { get; }
        public DateTime LastWriteUtc { get; }

        /// <summary>Solo metadati (nessun handle aperto sul file).</summary>
        public static FileMeta Read(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileMeta(true, info.Length, info.LastWriteTimeUtc) : new FileMeta(false, 0, default);
        }

        public bool Equals(FileMeta other) => Exists == other.Exists && Length == other.Length && LastWriteUtc == other.LastWriteUtc;
        public override bool Equals(object obj) => obj is FileMeta other && Equals(other);
        public override int GetHashCode() => LastWriteUtc.GetHashCode() ^ Length.GetHashCode();
    }
}
