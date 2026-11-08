using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Bluetech.Display.Recorder;

/// <summary>
/// Copie degli altri file che cambiano nella cartella registrata. L'08/11 Passepartout ha creato ScontrinoDigitale.txt
/// circa 5 s prima di scrivere in Scontrino.txt il QR dello scontrino digitale, e il suo contenuto non l'avevamo:
/// ora ogni contenuto nuovo di un altro file finisce in altri\ come NNNN_nome, con una riga "altro-file" nell'indice.
/// Gli originali si leggono soltanto, con condivisione completa: mai tolti, spostati, rinominati o bloccati
/// (Scontrino.txt in modalità consumo invece viene tolto). Per non copiare la scrittura a metà si aspetta che il file
/// smetta di cambiare e lo si legge con la stessa lettura stabile delle versioni.
/// </summary>
public sealed partial class RecordingSession
{
    const string OtherFolder = "altri";
    const int OtherSettleMilliseconds = 300;     // quiete dopo l'ultima notifica: la scrittura dovrebbe essere finita
    const int OtherMaxWaitMilliseconds = 2000;   // un file che non smette di cambiare si copia comunque dopo circa 2 s
    const int OtherUnstableRetries = 3;          // letture prese mentre il file cambiava: si riprova, poi si copia com'è
    const int OtherSkipsInSummary = 10;

    // Ordine dei lock: _otherWriteSync e poi _sync, mai il contrario.
    readonly object _otherWriteSync = new();
    readonly BlockingCollection<PendingCopy> _otherQueue = new();
    // Sotto _sync: copie in attesa che il file smetta di cambiare; copie fatte e ultima copia mancata (motivo) per nome.
    readonly Dictionary<string, PendingCopy> _otherPending = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _otherCopies = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _otherSkips = new(StringComparer.OrdinalIgnoreCase);
    // Solo il thread delle copie: hash dell'ultima copia di ogni file, numero e byte delle copie fatte.
    readonly Dictionary<string, string> _otherLastHash = new(StringComparer.OrdinalIgnoreCase);
    int _otherCopySeq;
    long _otherCopyBytes;
    Thread _otherWorker;
    string _otherLimit;     // scritto sotto _sync: limite raggiunto, niente più copie
    bool _otherClosed;      // sotto _otherWriteSync: sessione in chiusura, niente più copie né righe nell'indice

    /// <summary>Un altro file più grande non si copia: una riga "troppo grande" nell'indice.</summary>
    internal long MaxOtherFileBytes { get; set; } = 1024 * 1024;

    /// <summary>Copie di altri file in una sessione, al massimo.</summary>
    internal int MaxOtherCopies { get; set; } = 500;

    /// <summary>Byte copiati da altri file in una sessione, al massimo.</summary>
    internal long MaxOtherTotalBytes { get; set; } = 50L * 1024 * 1024;

    void StartOtherCopies()
    {
        _otherWorker = new Thread(OtherCopiesLoop) { IsBackground = true, Name = "Copie altri file" };
        _otherWorker.Start();
    }

    /// <summary>
    /// Chiusura, primo passo (le notifiche sono già ferme): le copie ancora in attesa si fanno subito, senza aspettare
    /// la pausa, e la coda viene smaltita.
    /// </summary>
    void FlushOtherCopies()
    {
        List<PendingCopy> pending;
        lock (_sync)
        {
            pending = _otherPending.Values.ToList();
            _otherPending.Clear();
            foreach (var copy in pending)
                QueueOtherCopyLocked(copy);
            _otherQueue.CompleteAdding();
        }
        foreach (var copy in pending)
            copy.Timer.Dispose();
    }

    /// <summary>
    /// Chiusura, secondo passo, prima del riepilogo e dello zip: si aspettano le copie in corso; da qui non ne parte più
    /// nessuna e nell'indice non si scrive più niente sugli altri file.
    /// </summary>
    void CloseOtherCopies()
    {
        _otherWorker?.Join(TimeSpan.FromSeconds(5));
        lock (_otherWriteSync)
            _otherClosed = true;
    }

    /// <summary>Un altro file della cartella è stato creato, modificato o rinominato: lo si copia appena smette di cambiare.</summary>
    void ScheduleOtherCopy(string name, string source)
    {
        // Solo file direttamente nella cartella (il watcher non guarda nelle sottocartelle, ma il nome si controlla lo stesso).
        if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name)
            return;
        lock (_sync)
            ScheduleOtherCopyLocked(name, new[] { source }, 0);
    }

    /// <summary>
    /// La copia parte <see cref="OtherSettleMilliseconds"/> dopo l'ultima notifica, ma non oltre
    /// <see cref="OtherMaxWaitMilliseconds"/> dalla prima. False se non si copia più niente (chiusura o limite raggiunto).
    /// </summary>
    bool ScheduleOtherCopyLocked(string name, IEnumerable<string> sources, int unstableRounds)
    {
        if (_stopping || _otherLimit != null)
            return false;
        var now = _clock.ElapsedMilliseconds;
        if (!_otherPending.TryGetValue(name, out var pending))
        {
            pending = new PendingCopy(name, now, unstableRounds);
            _otherPending.Add(name, pending);
            pending.Timer = new Timer(OnOtherFileSettled, pending, OtherSettleMilliseconds, Timeout.Infinite);
        }
        else if (now - pending.FirstMs < OtherMaxWaitMilliseconds)
        {
            pending.Timer.Change(OtherSettleMilliseconds, Timeout.Infinite);
        }
        foreach (var source in sources)
            pending.AddSource(source);
        return true;
    }

    void OnOtherFileSettled(object state)
    {
        var pending = (PendingCopy)state;
        lock (_sync)
        {
            // Già presa dalla chiusura (FlushOtherCopies): niente da fare.
            if (!_otherPending.TryGetValue(pending.Name, out var current) || current != pending)
                return;
            _otherPending.Remove(pending.Name);
            QueueOtherCopyLocked(pending);
        }
        pending.Timer.Dispose();
    }

    void QueueOtherCopyLocked(PendingCopy pending)
    {
        if (!_otherQueue.IsAddingCompleted)
            _otherQueue.Add(pending);
    }

    /// <summary>Un thread solo per le copie: non rallentano la registrazione di Scontrino.txt e non si sovrappongono.</summary>
    void OtherCopiesLoop()
    {
        foreach (var pending in _otherQueue.GetConsumingEnumerable())
        {
            try
            {
                CopyOtherFile(pending);
            }
            catch (Exception ex)
            {
                try
                {
                    SkipOther(pending, "errore: " + ex.Message);
                }
                catch (Exception) { }   // indice non scrivibile: lo dirà la registrazione delle versioni
            }
        }
    }

    /// <summary>Legge l'altro file senza ostacolare nessuno e, se il contenuto è nuovo per quel nome, lo copia in altri\.</summary>
    void CopyOtherFile(PendingCopy pending)
    {
        if (_otherLimit != null)
            return;
        var path = Path.Combine(Folder, pending.Name);
        if (!IsRegularFile(path, out var missing))
        {
            if (missing)
                SkipOther(pending, "sparito prima della copia");
            return;   // cartella o collegamento: si copiano solo i file
        }
        var size = FileMeta.Read(path).Length;
        if (size > MaxOtherFileBytes)
        {
            SkipTooLarge(pending, size);
            return;
        }

        var read = ReadStable(path, waitForWriter: false);
        if (read.Missing)
        {
            SkipOther(pending, "sparito prima della copia");
            return;
        }
        if (read.Locked)
        {
            SkipOther(pending, "non leggibile (bloccato da un altro programma o senza permessi)");
            return;
        }
        if (!read.Stable && pending.UnstableRounds < OtherUnstableRetries)
        {
            // Ancora in scrittura: si riprova dopo un'altra pausa. In chiusura si copia subito com'è, segnandolo.
            lock (_sync)
            {
                if (ScheduleOtherCopyLocked(pending.Name, pending.Sources, pending.UnstableRounds + 1))
                    return;
            }
        }
        if (read.Bytes.LongLength > MaxOtherFileBytes)
        {
            SkipTooLarge(pending, read.Bytes.LongLength);
            return;
        }

        var hash = Sha256(read.Bytes);
        if (_otherLastHash.TryGetValue(pending.Name, out var last) && last == hash)
            return;   // uguale all'ultima copia di questo file
        SaveOtherCopy(pending, read, hash);
    }

    /// <summary>File normale (non cartella né collegamento)? Se non c'è più, <paramref name="missing"/>.</summary>
    static bool IsRegularFile(string path, out bool missing)
    {
        missing = false;
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            missing = true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;   // lo dirà la lettura
        }
    }

    void SaveOtherCopy(PendingCopy pending, ReadResult read, string hash)
    {
        var bytes = read.Bytes;
        if (_otherCopySeq >= MaxOtherCopies)
        {
            StopOtherCopies(pending, $"limite di {MaxOtherCopies} copie raggiunto");
            return;
        }
        if (_otherCopyBytes + bytes.LongLength > MaxOtherTotalBytes)
        {
            StopOtherCopies(pending, $"limite di {Size(MaxOtherTotalBytes)} di copie raggiunto");
            return;
        }

        var seq = _otherCopySeq + 1;
        var fileName = $"{seq:0000}_{pending.Name}";
        var entry = NewEvent("altro-file")
            .With("nome", pending.Name)
            .With("copia", OtherFolder + "/" + fileName)
            .With("byte", bytes.Length)
            .With("sha256", hash.Substring(0, 16));
        DescribeOtherContent(bytes, entry);
        if (!read.Stable)
            entry.Add("stabile", false);   // preso mentre cambiava ancora (in chiusura o dopo più tentativi)
        entry.Add("ultimaModificaFile", FileTime(read.Meta.LastWriteUtc));
        entry.Add("origine", string.Join(",", pending.Sources));

        lock (_otherWriteSync)
        {
            if (_otherClosed)
                return;
            var folder = Path.Combine(SessionFolder, OtherFolder);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, fileName), bytes);
            _otherCopySeq = seq;
            _otherCopyBytes += bytes.LongLength;
            _otherLastHash[pending.Name] = hash;
            lock (_sync)
            {
                _otherCopies[pending.Name] = _otherCopies.TryGetValue(pending.Name, out var count) ? count + 1 : 1;
                _otherSkips.Remove(pending.Name);
                WriteIndexLocked(entry);
            }
        }
        _log.Line($"{Clock()}  cartella: copiato {pending.Name} ({bytes.Length} byte) → {Path.Combine(OtherFolder, fileName)}", ConsoleColor.DarkGray);
    }

    /// <summary>
    /// Per i file di testo codifica, fine riga e righe, come per le versioni (FileAnalyzer); "binario" se ci sono byte
    /// nulli senza il BOM di UTF-16.
    /// </summary>
    static void DescribeOtherContent(byte[] bytes, JsonObject entry)
    {
        var utf16 = bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF));
        if (!utf16 && Array.IndexOf(bytes, (byte)0) >= 0)
        {
            entry.Add("codifica", "binario");
            return;
        }
        var analysis = FileAnalyzer.Analyze(bytes);
        entry.Add("codifica", analysis.Encoding);
        entry.Add("fineRiga", analysis.LineEndings);
        entry.Add("righeFile", analysis.Lines.Length);
    }

    void SkipTooLarge(PendingCopy pending, long size)
    {
        if (SkipOther(pending, "troppo grande", size))
            _log.Line($"{Clock()}  cartella: {pending.Name} non copiato, troppo grande ({Size(size)}, limite {Size(MaxOtherFileBytes)})", ConsoleColor.DarkGray);
    }

    /// <summary>
    /// Copia non fatta: una riga nell'indice col motivo, niente a video. Lo stesso motivo per lo stesso file non si
    /// ripete finché non c'è una copia riuscita. True se la riga è stata scritta.
    /// </summary>
    bool SkipOther(PendingCopy pending, string reason, long? size = null)
    {
        var entry = OtherSkipEvent(pending, reason, size);
        lock (_otherWriteSync)
        {
            if (_otherClosed)
                return false;
            lock (_sync)
            {
                if (_otherSkips.TryGetValue(pending.Name, out var previous) && previous == reason)
                    return false;
                _otherSkips[pending.Name] = reason;
                WriteIndexLocked(entry);
                return true;
            }
        }
    }

    /// <summary>Limite raggiunto: una riga nell'indice e un avviso, poi più nessuna copia.</summary>
    void StopOtherCopies(PendingCopy pending, string limit)
    {
        lock (_otherWriteSync)
        {
            if (_otherClosed)
                return;
            lock (_sync)
            {
                _otherLimit = limit;
                WriteIndexLocked(OtherSkipEvent(pending, limit + ": niente più copie di altri file", null));
            }
        }
        _log.Line($"{Clock()}  Avviso: {limit}, non copio più gli altri file della cartella", ConsoleColor.DarkYellow);
    }

    JsonObject OtherSkipEvent(PendingCopy pending, string reason, long? size)
    {
        var entry = NewEvent("altro-file").With("nome", pending.Name).With("copia", null).With("motivo", reason);
        if (size != null)
        {
            entry.Add("byte", size.Value);
            entry.Add("limiteByte", MaxOtherFileBytes);
        }
        entry.Add("origine", string.Join(",", pending.Sources));
        return entry;
    }

    /// <summary>Righe del riepilogo sulle copie degli altri file (con _sync preso).</summary>
    void AppendOtherCopiesLocked(StringBuilder sb)
    {
        var copies = _otherCopies.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{p.Key} {p.Value}").ToList();
        sb.AppendLine("  Copie di altri file: " + (copies.Count == 0 ? "-" : string.Join(", ", copies)));
        if (_otherSkips.Count > 0)
        {
            var skipped = _otherSkips.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Take(OtherSkipsInSummary).Select(p => $"{p.Key} ({p.Value})");
            var more = _otherSkips.Count > OtherSkipsInSummary ? $" e altri {_otherSkips.Count - OtherSkipsInSummary}" : "";
            sb.AppendLine("  Altri file non copiati: " + string.Join(", ", skipped) + more);
        }
        if (_otherLimit != null)
            sb.AppendLine("  Copie di altri file interrotte: " + _otherLimit);
    }

    /// <summary>"1 MB", "50 MB", "1,5 MB", "12 KB", "300 byte".</summary>
    static string Size(long bytes)
    {
        const long Kb = 1024;
        const long Mb = 1024 * 1024;
        if (bytes >= Mb)
            return bytes % Mb == 0
                ? $"{bytes / Mb} MB"
                : (bytes / (double)Mb).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " MB";
        if (bytes >= Kb)
            return $"{(bytes + Kb - 1) / Kb} KB";
        return $"{bytes} byte";
    }

    /// <summary>Un altro file da copiare appena smette di cambiare.</summary>
    sealed class PendingCopy
    {
        public PendingCopy(string name, long firstMs, int unstableRounds)
        {
            Name = name;
            FirstMs = firstMs;
            UnstableRounds = unstableRounds;
        }

        public string Name { get; }
        public long FirstMs { get; }          // prima notifica, in ms dall'inizio della sessione
        public int UnstableRounds { get; }    // letture già prese mentre il file cambiava
        public Timer Timer { get; set; }

        /// <summary>Le notifiche arrivate (creato, modificato, rinominato), per l'indice.</summary>
        public List<string> Sources { get; } = new();

        public void AddSource(string source)
        {
            if (!Sources.Contains(source))
                Sources.Add(source);
        }
    }
}
