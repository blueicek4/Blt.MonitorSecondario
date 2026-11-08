using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

/// <summary>Sessioni vere su disco: simulano Passepartout che riscrive Scontrino.txt.</summary>
public sealed class RecordingSessionTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-recorder-" + Guid.NewGuid().ToString("N"));
    readonly string _target;
    readonly string _output;
    readonly TestLog _log = new();

    public RecordingSessionTests()
    {
        _target = Path.Combine(_root, "MonitorSecondario", "Scontrino.txt");
        _output = Path.Combine(_root, "MonitorSecondario", "Registrazioni");
        Directory.CreateDirectory(Path.GetDirectoryName(_target));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    static byte[] Receipt(params string[] items)
    {
        var lines = new List<string> { "SNF", " " };
        lines.AddRange(items);
        lines.Add("TOT. DA PAGARE           9,99");
        return Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n");
    }

    void Write(byte[] bytes)
    {
        // Come un programma che riscrive il file per intero, con i ritentativi di Passepartout (3 × 1 s).
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(_target, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(bytes, 0, bytes.Length);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>
    /// Apertura in scrittura che riprova, come fa Passepartout: per capire se una scrittura è finita il registratore apre
    /// il file per un istante negando la scrittura, e uno scrittore che arriva proprio allora trova il file occupato.
    /// </summary>
    FileStream OpenForWriting()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(_target, FileMode.Create, FileAccess.Write, FileShare.Read);
            }
            catch (IOException) when (attempt < 50)
            {
                Thread.Sleep(10);
            }
        }
    }

    static void WaitUntil(Func<bool> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("Attesa scaduta: " + what);
            Thread.Sleep(20);
        }
    }

    /// <summary>L'indice è aperto in scrittura dal registratore: su Windows va letto condividendo la scrittura.</summary>
    static List<string> IndexLines(RecordingSession session)
    {
        using var stream = new FileStream(Path.Combine(session.SessionFolder, "eventi.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        string line;
        while ((line = reader.ReadLine()) != null)
            lines.Add(line);
        return lines;
    }

    static List<string> VersionFiles(RecordingSession session) =>
        Directory.GetFiles(session.SessionFolder, "0*.txt").ToList();

    [Fact]
    public void Records_every_distinct_version_with_diffs_and_creates_the_zip()
    {
        Write(Receipt(" CAPPUCCINO 1,80"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start("PC di prova");
        WaitUntil(() => session.Versions == 1, "stato iniziale");

        Write(Receipt(" CAPPUCCINO 1,80", " CAPPUCCINO 1,80"));
        WaitUntil(() => session.Versions == 2, "seconda versione");

        Thread.Sleep(50);  // la data di modifica deve cambiare
        Write(Receipt(" CAPPUCCINO 1,80", " CAPPUCCINO 1,80"));
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"riscrittura\"")), "riscrittura identica");

        Write(Encoding.ASCII.GetBytes("TOTALE\r\n3,60\r\n"));
        WaitUntil(() => session.Versions == 3, "messaggio visore");

        File.Delete(_target);
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file eliminato");

        session.AddNote("pagamento con Satispay");
        session.Stop();

        Assert.Equal(3, session.Versions);
        var lines = IndexLines(session);
        Assert.Contains(lines, l => l.Contains("\"tipo\":\"inizio\""));
        Assert.Contains(lines, l => l.Contains("\"tipo\":\"fine\""));
        Assert.Contains(lines, l => l.Contains("\"testo\":\"pagamento con Satispay\""));
        var second = lines.Single(l => l.Contains("\"seq\":2,"));
        Assert.Contains("\"aggiunte\":[{\"riga\":2,\"testo\":\"CAPPUCCINO 1,80\"}]", second);
        Assert.Contains("\"rimosse\":[]", second);

        Assert.True(File.Exists(session.ZipPath));
        using var zip = ZipFile.OpenRead(session.ZipPath);
        var names = zip.Entries.Select(e => e.Name).ToList();
        Assert.Equal(3, names.Count(n => n.StartsWith("000") && n.EndsWith(".txt")));
        Assert.Contains("eventi.jsonl", names);
        Assert.Contains("riepilogo.txt", names);
        Assert.Contains("sessione.txt", names);

        // Le copie sono identiche byte per byte a quanto scritto.
        var first = Directory.GetFiles(session.SessionFolder, "0001_*.txt").Single();
        Assert.Equal(Receipt(" CAPPUCCINO 1,80"), File.ReadAllBytes(first));
    }

    [Fact]
    public void Waiting_for_a_file_that_does_not_exist_yet()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");

        Write(Receipt(" CAFFE 1,00"));
        WaitUntil(() => session.Versions == 1, "file creato dopo l'avvio");
        session.Stop();
        Assert.Contains(IndexLines(session), l => l.Contains("\"evento\":\"creato\"") || l.Contains("\"seq\":1,"));
    }

    [Fact]
    public void Qr_image_is_saved_next_to_the_version()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        Write(Encoding.ASCII.GetBytes("SNF\r\nQRCODE\r\niVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==\r\n"));
        WaitUntil(() => session.Versions == 1, "QR");
        session.Stop();
        Assert.Single(Directory.GetFiles(session.SessionFolder, "0001_qrcode.png"));
    }

    [Fact]
    public void Rapid_rewrites_never_block_the_writer_and_the_last_state_is_recorded()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var items = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            items.Add($" ARTICOLO {i,2}            1,00");
            Write(Receipt(items.ToArray()));   // fallirebbe dopo 3 tentativi se il registratore bloccasse il file
            Thread.Sleep(15);
        }
        var final = Receipt(items.ToArray());
        WaitUntil(() => VersionFiles(session).Any(f => File.ReadAllBytes(f).SequenceEqual(final)), "ultima versione");
        session.Stop();
        Assert.InRange(session.Versions, 1, 40);
    }

    [Fact]
    public async Task Stop_from_two_threads_waits_for_the_zip()
    {
        Write(Receipt(" CAFFE 1,00"));
        var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "stato iniziale");

        var stops = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            session.Stop();
            return session.ZipPath;
        })).ToArray();
        var zips = await Task.WhenAll(stops);
        Assert.All(zips, z => Assert.True(File.Exists(z)));
    }

    [Fact]
    public void Notes_after_stop_are_ignored()
    {
        var session = new RecordingSession(_target, _output, _log);
        session.Start();
        session.Stop();
        session.AddNote("troppo tardi");
        Assert.DoesNotContain(IndexLines(session), l => l.Contains("troppo tardi"));
    }

    [Fact]
    public void Content_read_while_the_writer_is_still_writing_never_becomes_a_version()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var partials = new List<byte[]>();
        byte[] full = null;
        for (var i = 0; i < 15; i++)
        {
            full = Receipt($" ARTICOLO {i,2}            1,00", " CAFFE                    1,10");
            var cut = full.Length / 2;
            partials.Add(full.Take(cut).ToArray());
            // Scrittore lento: metà conto, una pausa, il resto (lascia leggere gli altri, come fa .NET di default).
            // Flush senza fsync: la pausa deve essere di 5 ms, non quanto ci mette il disco.
            using (var stream = OpenForWriting())
            {
                stream.Write(full, 0, cut);
                stream.Flush();
                Thread.Sleep(5);
                stream.Write(full, cut, full.Length - cut);
            }
            Thread.Sleep(60);
        }

        WaitUntil(() => VersionFiles(session).Any(f => File.ReadAllBytes(f).SequenceEqual(full)), "ultimo conto completo");
        session.Stop();
        foreach (var file in VersionFiles(session))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.True(bytes.Length == 0 || !partials.Any(p => p.SequenceEqual(bytes)), "versione parziale registrata: " + Path.GetFileName(file));
        }
        Assert.DoesNotContain(VersionFiles(session), f => new FileInfo(f).Length == 0);
    }

    [Fact]
    public void On_windows_a_writer_pausing_longer_than_the_stability_window_is_still_waited_for()
    {
        // Su Linux la condivisione dei file non è imposta: lì vale solo il controllo sui tempi (test precedente).
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return;

        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var partials = new List<byte[]>();
        byte[] full = null;
        for (var i = 0; i < 5; i++)
        {
            full = Receipt($" ARTICOLO {i,2}            1,00");
            var cut = full.Length / 2;
            partials.Add(full.Take(cut).ToArray());
            using (var stream = OpenForWriting())
            {
                Thread.Sleep(80);                       // file vuoto e aperto in scrittura
                stream.Write(full, 0, cut);
                stream.Flush(true);
                Thread.Sleep(80);                       // metà conto, ancora aperto
                stream.Write(full, cut, full.Length - cut);
            }
            Thread.Sleep(300);
        }

        WaitUntil(() => VersionFiles(session).Any(f => File.ReadAllBytes(f).SequenceEqual(full)), "ultimo conto completo");
        session.Stop();
        foreach (var file in VersionFiles(session))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.NotEmpty(bytes);
            Assert.DoesNotContain(partials, p => p.SequenceEqual(bytes));
        }
    }

    [Fact]
    public void Version_entries_carry_file_size_and_time_written_after_the_read()
    {
        Write(Receipt(" CAFFE 1,00"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "versione");
        session.Stop();
        Assert.Contains("\"attributiFile\":\"", IndexLines(session).Single(l => l.Contains("\"tipo\":\"inizio\"")));
        var entry = IndexLines(session).Single(l => l.Contains("\"seq\":1,"));
        Assert.Contains($"\"byteMetadati\":{Receipt(" CAFFE 1,00").Length},", entry);
        Assert.Contains("\"ultimaModificaFile\":\"", entry);
        Assert.Contains("\"creazioneFile\":\"", entry);
        Assert.Contains("\"righe\":1,", entry);
    }

    /// <summary>
    /// Uno scrittore che non sovrascrive: scrive solo se il file non c'è, altrimenti ritenta (3 volte) e poi rinuncia.
    /// Restituisce true se ha scritto.
    /// Il contenuto compare tutto insieme (file temporaneo spostato al suo posto): su Linux .NET prende il lock
    /// dopo aver creato il file, e un lettore che arriva in quell'istante farebbe fallire lo scrittore.
    /// Su Windows il caso del file vuoto appena creato è coperto dal controllo sullo scrittore ancora aperto.
    /// </summary>
    bool WriteIfAbsent(byte[] bytes, int attempts = 3, int waitMilliseconds = 300)
    {
        var temp = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temp, bytes);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                File.Move(temp, _target);
                return true;
            }
            catch (IOException) when (File.Exists(_target))
            {
                Thread.Sleep(waitMilliseconds);
            }
        }
        File.Delete(temp);
        return false;
    }

    /// <summary>
    /// Come Passepartout il 20/10: crea il file vuoto e lo riempie qualche millisecondo dopo. Se il file c'è già
    /// rinuncia, come un Passepartout che non sovrascrive. Restituisce true se ha scritto.
    /// </summary>
    bool CreateEmptyThenFill(byte[] bytes, int pauseMilliseconds = 5)
    {
        try
        {
            using var stream = new FileStream(_target, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            Thread.Sleep(pauseMilliseconds);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (IOException) when (File.Exists(_target))
        {
            return false;
        }
    }

    static void WaitForVersion(RecordingSession session, byte[] bytes, string what) =>
        WaitUntil(() => VersionFiles(session).Any(f => File.ReadAllBytes(f).SequenceEqual(bytes)), what);

    [Fact]
    public void Ab_test_with_a_writer_that_never_overwrites_keeps_pointing_at_the_cause()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");

        // A: il primo conto entra (il file non c'era), il secondo no (il file c'è e lo scrittore rinuncia).
        var first = Receipt(" A1                       1,00");
        Assert.True(CreateEmptyThenFill(first));
        WaitForVersion(session, first, "fase A");
        session.AddNote("A");
        Assert.False(CreateEmptyThenFill(Receipt(" A2                       1,00")));
        session.AddNote("A, secondo articolo");

        // B: con /consuma ogni scrittura entra; il file nuovo si può prendere ancora vuoto.
        Assert.True(session.SetConsumeMode(true));
        WaitUntil(() => !File.Exists(_target), "file tolto all'attivazione");
        for (var i = 0; i < 4; i++)
        {
            var bytes = Receipt($" B{i}                       1,00");
            Assert.True(CreateEmptyThenFill(bytes), "scrittura B" + i);
            WaitForVersion(session, bytes, "fase B " + i);
            WaitUntil(() => !File.Exists(_target), "versione B" + i + " tolta");
        }

        // A2: /consuma spento; il primo entra (il file non c'è), il secondo no.
        Assert.True(session.SetConsumeMode(false));
        var again = Receipt(" C1                       1,00");
        Assert.True(CreateEmptyThenFill(again));
        WaitForVersion(session, again, "fase A2");
        session.AddNote("A2");
        Assert.False(CreateEmptyThenFill(Receipt(" C2                       1,00")));
        session.AddNote("A2, secondo articolo");
        session.Stop();

        var warnings = IndexLines(session).Where(l => l.Contains("\"tipo\":\"avviso\"")).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.Contains("\"caso\":\"mai-riscritto\"", w));
        Assert.Contains("ha sostituito da sé il file che c'era: no", File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt")));
    }

    [Fact]
    public void Startup_read_of_an_old_file_is_not_timed_as_a_write()
    {
        Write(Receipt(" CONTO VECCHIO            1,00"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");
        File.Delete(_target);
        var next = Receipt(" CAFFE                    1,10");
        Write(next);
        WaitForVersion(session, next, "file ricreato");
        session.Stop();
        Assert.Contains("Tempo minimo tra due scritture: - (con /consuma: -)", File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt")));
    }

    [Fact]
    public void Without_consume_mode_a_no_overwrite_writer_gives_up_and_the_operator_is_warned()
    {
        Write(Receipt(" CONTO PRECEDENTE         1,00"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");

        session.AddNote("inserimento prodotti");
        Assert.False(WriteIfAbsent(Receipt(" CAFFE                    1,10")));
        session.AddNote("passo al conto");
        session.Stop();

        Assert.Equal(1, session.Versions);
        Assert.True(_log.Contains("Attenzione: dalla nota precedente"));
        Assert.True(_log.Contains("SovrascriviSeEsistente=false"));
        Assert.Contains(IndexLines(session), l => l.Contains("\"tipo\":\"avviso\"") && l.Contains("\"caso\":\"mai-riscritto\""));
    }

    [Fact]
    public void Consume_mode_removes_each_version_so_a_no_overwrite_writer_can_continue()
    {
        Write(Receipt(" CONTO PRECEDENTE         1,00"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");
        session.SetConsumeMode(true);
        WaitUntil(() => !File.Exists(_target), "file tolto all'attivazione");

        session.AddNote("tre articoli");
        var items = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            items.Add($" PIATTO {i}                 1,00");
            Assert.True(WriteIfAbsent(Receipt(items.ToArray())), "scrittura " + i);
            var expected = i + 2;
            WaitUntil(() => session.Versions == expected && !File.Exists(_target), "versione " + expected + " registrata e tolta");
        }

        // Lo stesso conto rimandato identico è una riscrittura, non una versione nuova.
        Assert.True(WriteIfAbsent(Receipt(items.ToArray())));
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"riscrittura\"")) && !File.Exists(_target), "riscrittura tolta");
        session.AddNote("fine prova");
        session.Stop();

        var lines = IndexLines(session);
        Assert.Equal(4, session.Versions);
        Assert.Equal(5, lines.Count(l => l.Contains("\"tipo\":\"consumato\"")));
        Assert.DoesNotContain(lines, l => l.Contains("\"tipo\":\"assente\""));
        Assert.DoesNotContain(lines, l => l.Contains("\"tipo\":\"avviso\""));
        Assert.Contains("\"aggiunte\":[{\"riga\":3,\"testo\":\"PIATTO 2                 1,00\"}]", lines.Single(l => l.Contains("\"seq\":4,")));
    }

    [Fact]
    public void Consume_mode_never_removes_a_version_it_has_not_recorded()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");
        Assert.True(session.SetConsumeMode(true));

        // Passepartout che sovrascrive proprio tra la lettura di #0001 e il momento di toglierlo.
        var first = Receipt(" PIATTO 1                 1,00");
        var second = Receipt(" PIATTO 1                 1,00", " PIATTO 2                 1,00");
        var overwritten = 0;
        _log.OnLine = text =>
        {
            if (text.Contains("#0001") && Interlocked.Exchange(ref overwritten, 1) == 0)
                Write(second);
        };
        Write(first);

        WaitUntil(() => session.Versions == 2 && !File.Exists(_target), "seconda versione registrata e poi tolta");
        session.Stop();
        Assert.Equal(second, File.ReadAllBytes(Directory.GetFiles(session.SessionFolder, "0002_*.txt").Single()));
        Assert.Contains(IndexLines(session), l => l.Contains("\"tipo\":\"consumoRinviato\""));
    }

    [Fact]
    public void Consume_mode_is_refused_while_the_old_monitor_reads_the_same_file()
    {
        using var session = new RecordingSession(_target, _output, _log)
        {
            FindOldMonitors = () => new[] { Path.Combine(Path.GetDirectoryName(_target), "MonitorCliente.exe") },
        };
        session.Start();

        Assert.False(session.SetConsumeMode(true));
        Assert.False(session.ConsumeMode);
        Assert.True(_log.Contains("NON attivata"));

        Assert.True(session.SetConsumeMode(true, force: true));
        Assert.True(session.ConsumeMode);
        session.Stop();
        var lines = IndexLines(session);
        Assert.Contains(lines, l => l.Contains("\"rifiutato\":\"vecchio monitor aperto\""));
        Assert.Contains(lines, l => l.Contains("\"forzato\":true"));
    }

    [Fact]
    public void Consume_mode_is_refused_when_the_old_monitor_folder_is_unknown()
    {
        using var session = new RecordingSession(_target, _output, _log) { FindOldMonitors = () => new[] { "MonitorCliente.exe" } };
        session.Start();
        Assert.False(session.SetConsumeMode(true));
        session.Stop();
    }

    [Fact]
    public void Old_monitor_reading_another_folder_is_reported_and_does_not_block_consume_mode()
    {
        var otherFolder = Path.Combine(_root, "Passepartout", "Monitor");
        Directory.CreateDirectory(otherFolder);
        File.WriteAllText(Path.Combine(otherFolder, "MonitorCliente.exe.config"), "<configuration />");
        Write(Receipt(" CONTO VECCHIO            1,00"));
        using var session = new RecordingSession(_target, _output, _log)
        {
            FindOldMonitors = () => new[] { Path.Combine(otherFolder, "MonitorCliente.exe") },
        };
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");

        session.AddNote("primo articolo");
        session.AddNote("secondo articolo");
        Assert.True(_log.Contains("Il vecchio monitor aperto legge " + Path.Combine(otherFolder, "Scontrino.txt")));
        Assert.True(session.SetConsumeMode(true));
        session.Stop();

        Assert.Contains(IndexLines(session), l => l.Contains("\"tipo\":\"inizio\"") && l.Contains("\"vecchioMonitor\":[\""));
        Assert.True(File.Exists(Path.Combine(session.SessionFolder, "contesto", "vecchio-monitor_MonitorCliente.exe.config")));
    }

    [Fact]
    public void Warning_without_the_file_points_to_the_path()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");
        session.AddNote("primo articolo");
        session.AddNote("secondo articolo");
        session.Stop();
        Assert.True(_log.Contains("il percorso deve essere " + _target));
        Assert.Contains(IndexLines(session), l => l.Contains("\"caso\":\"file-assente\""));
    }

    [Fact]
    public void No_alarm_once_Passepartout_has_overwritten_the_file()
    {
        Write(Receipt(" CAFFE                    1,10"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");
        Write(Receipt(" CAFFE                    1,10", " CAFFE                    1,10"));
        WaitUntil(() => session.Versions == 2, "file sovrascritto");

        session.AddNote("preconto");
        session.AddNote("ancora preconto");
        session.Stop();
        Assert.False(_log.Contains("Attenzione"));
        Assert.True(_log.Contains("ha già riscritto il file"));
        Assert.Contains(IndexLines(session), l => l.Contains("\"caso\":\"sovrascrittura-gia-vista\""));
        Assert.Contains("ha sostituito da sé il file che c'era: sì", File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt")));
    }

    [Fact]
    public void Deleting_and_recreating_the_file_counts_as_replacing_it()
    {
        // Come Passepartout nella registrazione del 20/10: elimina il file e lo ricrea a ogni scrittura.
        Write(Receipt(" CAFFE                    1,10"));
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => session.Versions == 1, "file già presente");
        File.Delete(_target);
        Thread.Sleep(100);
        Write(Receipt(" CAFFE                    1,10", " ACQUA                    1,00"));
        WaitUntil(() => session.Versions == 2, "file ricreato");
        File.Delete(_target);
        Thread.Sleep(100);
        Write(Receipt(" CAFFE                    1,10", " ACQUA                    1,00", " ACQUA                    1,00"));
        WaitUntil(() => session.Versions == 3, "file ricreato di nuovo");

        session.AddNote("preconto");
        session.AddNote("ancora preconto");
        session.Stop();
        Assert.False(_log.Contains("Attenzione"));
        var summary = File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt"));
        Assert.Contains("ha sostituito da sé il file che c'era: sì", summary);
        Assert.DoesNotContain("Tempo minimo tra due scritture: -", summary);
        Assert.Contains("(con /consuma: -)", summary);
    }

    [Fact]
    public void Consume_mode_with_nothing_written_since_it_was_turned_on_points_to_the_path()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");
        session.SetConsumeMode(true);
        session.AddNote("primo articolo");
        session.AddNote("secondo articolo");
        session.Stop();
        Assert.True(_log.Contains("Da quando hai scritto /consuma non è arrivato niente"));
        Assert.Contains(IndexLines(session), l => l.Contains("\"caso\":\"consumo-niente\""));
    }

    [Fact]
    public void Consume_mode_after_some_writes_calls_a_pause_normal()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"assente\"")), "file assente all'avvio");
        session.SetConsumeMode(true);
        Assert.True(WriteIfAbsent(Receipt(" CAFFE                    1,10")));
        WaitUntil(() => session.Versions == 1 && !File.Exists(_target), "versione registrata e tolta");

        session.AddNote("preconto");
        session.AddNote("ancora preconto");
        session.Stop();
        Assert.False(_log.Contains("Attenzione"));
        Assert.Contains(IndexLines(session), l => l.Contains("\"caso\":\"consumo-normale\""));
    }

    [Theory]
    [InlineData(40, "40 s")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(100 * 60, "1 ora")]
    [InlineData(5 * 3600, "5 ore")]
    [InlineData(3 * 86400, "3 giorni")]
    public void Ages_are_said_in_plain_italian(int seconds, string expected)
    {
        Assert.Equal(expected, Timestamps.Age(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Replaying_the_recording_of_20_october_gives_back_every_version()
    {
        // I primi due conti (#01–#16: due versioni rimandate identiche, dettagli, pagamenti), scritti come faceva
        // Passepartout: elimina il file e lo ricrea 250 ms dopo. Le pause tra una battuta e l'altra sono accorciate;
        // il contenuto compare tutto insieme (vedi WriteIfAbsent).
        var source = Path.Combine(AppContext.BaseDirectory, "samples", "registrazioni", "2026-10-20_002138");
        var versions = Directory.GetFiles(source, "00*.txt").OrderBy(f => f, StringComparer.Ordinal).Take(16).Select(File.ReadAllBytes).ToList();

        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        for (var i = 0; i < versions.Count; i++)
        {
            if (File.Exists(_target))
            {
                File.Delete(_target);
                Thread.Sleep(250);
            }
            Assert.True(WriteIfAbsent(versions[i]));
            var expected = i + 1;
            WaitUntil(() => session.Versions == expected, "versione " + expected);
        }
        session.Stop();

        var recorded = VersionFiles(session).OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllBytes).ToList();
        Assert.Equal(versions.Count, recorded.Count);
        for (var i = 0; i < versions.Count; i++)
            Assert.True(versions[i].SequenceEqual(recorded[i]), $"versione {i + 1} diversa");
        var lines = IndexLines(session);
        Assert.Contains(lines, l => l.Contains("\"seq\":8,") && l.Contains("\"uguale\":7,"));
        Assert.Contains("ha sostituito da sé il file che c'era: sì", File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt")));
    }

    [Fact]
    public void No_warning_when_the_file_changed_between_notes()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        session.AddNote("primo articolo");
        Write(Receipt(" CAFFE 1,00"));
        WaitUntil(() => session.Versions == 1, "versione");
        session.AddNote("secondo articolo");
        session.Stop();
        Assert.False(_log.Contains("Attenzione"));
    }

    // Altri file della cartella (ScontrinoDigitale.txt, 08/11): copiati in altri\, mai toccati.

    string OtherFile(string name) => Path.Combine(Path.GetDirectoryName(_target), name);

    static List<string> OtherCopies(RecordingSession session)
    {
        var folder = Path.Combine(session.SessionFolder, "altri");
        return Directory.Exists(folder) ? Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string>();
    }

    static List<string> OtherFileLines(RecordingSession session) =>
        IndexLines(session).Where(l => l.Contains("\"tipo\":\"altro-file\"")).ToList();

    static List<string> OtherCopyLines(RecordingSession session) =>
        OtherFileLines(session).Where(l => l.Contains("\"copia\":\"")).ToList();

    static string Summary(RecordingSession session) => File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt"));

    static string ShortHash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))).Substring(0, 16);
    }

    [Fact]
    public void Another_file_written_in_the_folder_is_copied_and_left_untouched_even_in_consume_mode()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        Assert.True(session.SetConsumeMode(true));
        // Una sottocartella e il suo contenuto non si copiano.
        var subfolder = OtherFile("Sottocartella");
        Directory.CreateDirectory(subfolder);
        File.WriteAllText(Path.Combine(subfolder, "dentro.txt"), "non va copiato");

        var digital = OtherFile("ScontrinoDigitale.txt");
        var content = Encoding.ASCII.GetBytes("SCONTRINO DIGITALE\r\nhttps://esempio.invalid/d/0001\r\n");
        File.WriteAllBytes(digital, content);
        var written = File.GetLastWriteTimeUtc(digital);
        WaitUntil(() => OtherCopyLines(session).Count == 1, "copia di ScontrinoDigitale.txt");

        // Scontrino.txt in modalità consumo viene tolto dopo la lettura, l'altro file no.
        Write(Receipt(" CAFFE 1,00"));
        WaitUntil(() => session.Versions == 1 && !File.Exists(_target), "versione registrata e tolta");
        session.Stop();

        Assert.Equal(content, File.ReadAllBytes(digital));
        Assert.Equal(written, File.GetLastWriteTimeUtc(digital));
        var copy = Path.Combine(session.SessionFolder, "altri", "0001_ScontrinoDigitale.txt");
        Assert.Equal(new[] { copy }, OtherCopies(session));
        Assert.Equal(content, File.ReadAllBytes(copy));

        var line = OtherFileLines(session).Single();
        Assert.Contains("\"nome\":\"ScontrinoDigitale.txt\"", line);
        Assert.Contains("\"copia\":\"altri/0001_ScontrinoDigitale.txt\"", line);
        Assert.Contains($"\"byte\":{content.Length},", line);
        Assert.Contains($"\"sha256\":\"{ShortHash(content)}\"", line);
        Assert.Contains("\"codifica\":\"ASCII\",\"fineRiga\":\"CRLF\",\"righeFile\":2,", line);
        Assert.True(_log.Contains("cartella: copiato ScontrinoDigitale.txt"));
        Assert.Contains("Copie di altri file: ScontrinoDigitale.txt 1", Summary(session));
    }

    [Fact]
    public void Another_file_is_copied_again_only_when_its_content_changes()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var digital = OtherFile("ScontrinoDigitale.txt");
        var first = Encoding.ASCII.GetBytes("PRIMO\r\n");
        File.WriteAllBytes(digital, first);
        WaitUntil(() => OtherCopies(session).Count == 1, "prima copia");

        // Eliminato e ricreato identico (come alle 18:09 dell'08/11): nessuna copia nuova.
        File.Delete(digital);
        File.WriteAllBytes(digital, first);
        Thread.Sleep(600);   // oltre la pausa prima della copia: la riscrittura identica è già stata letta
        var second = Encoding.UTF8.GetBytes("SECONDO, è diverso\r\n");
        File.WriteAllBytes(digital, second);
        WaitUntil(() => OtherCopies(session).Count == 2, "seconda copia");
        session.Stop();

        var copies = OtherCopies(session);
        Assert.Equal(2, copies.Count);
        Assert.Equal(first, File.ReadAllBytes(copies[0]));
        Assert.Equal(second, File.ReadAllBytes(copies[1]));
        Assert.EndsWith("0002_ScontrinoDigitale.txt", copies[1]);
        var lines = OtherCopyLines(session);
        Assert.Equal(2, lines.Count);
        Assert.Contains("\"codifica\":\"UTF-8 senza BOM\"", lines[1]);
        Assert.Contains("Copie di altri file: ScontrinoDigitale.txt 2", Summary(session));
    }

    [Fact]
    public void Another_file_over_the_size_limit_is_not_copied()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var big = OtherFile("Grande.log");
        const int size = 1024 * 1024 + 1;
        File.WriteAllBytes(big, new byte[size]);
        WaitUntil(() => OtherFileLines(session).Any(l => l.Contains("\"motivo\":\"troppo grande\"")), "file troppo grande");
        session.Stop();

        Assert.Empty(OtherCopies(session));
        Assert.Equal(size, new FileInfo(big).Length);
        var line = OtherFileLines(session).Single();   // una riga sola, anche con più notifiche
        Assert.Contains("\"copia\":null", line);
        Assert.Contains($"\"byte\":{size},\"limiteByte\":{1024 * 1024}", line);
        Assert.True(_log.Contains("Grande.log non copiato, troppo grande"));
        Assert.Contains("Altri file non copiati: Grande.log (troppo grande)", Summary(session));
    }

    [Fact]
    public void Copies_of_other_files_stop_at_the_session_limit()
    {
        using var session = new RecordingSession(_target, _output, _log) { MaxOtherCopies = 2 };
        session.Start();
        for (var i = 1; i <= 3; i++)
        {
            File.WriteAllText(OtherFile($"Altro{i}.txt"), "contenuto " + i);
            var expected = i;
            WaitUntil(() => OtherFileLines(session).Count == expected, "riga " + expected);
        }
        // Dopo il limite non si copia più niente: la notifica arriva, la copia no.
        File.WriteAllText(OtherFile("Altro4.txt"), "contenuto 4");
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"cartella\"") && l.Contains("\"nome\":\"Altro4.txt\"")), "notifica di Altro4.txt");
        session.Stop();

        Assert.Equal(2, OtherCopies(session).Count);
        var lines = OtherFileLines(session);
        Assert.Equal(3, lines.Count);
        Assert.Contains("\"nome\":\"Altro3.txt\",\"copia\":null,\"motivo\":\"limite di 2 copie raggiunto: niente più copie di altri file\"", lines[2]);
        Assert.True(_log.Contains("limite di 2 copie raggiunto"));
        Assert.Contains("Copie di altri file interrotte: limite di 2 copie raggiunto", Summary(session));
    }

    [Fact]
    public void Another_file_gone_before_the_copy_leaves_one_index_line()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var temp = OtherFile("Passeggero.tmp");
        File.WriteAllText(temp, "dura poco");
        File.Delete(temp);
        WaitUntil(() => OtherFileLines(session).Count > 0, "riga dell'altro file");
        session.Stop();

        Assert.Empty(OtherCopies(session));
        Assert.Contains("\"motivo\":\"sparito prima della copia\"", OtherFileLines(session).Single());
        Assert.False(_log.Contains("copiato Passeggero.tmp"));
    }

    [Fact]
    public void A_copy_still_waiting_at_stop_ends_up_in_the_zip_and_nothing_is_written_after()
    {
        using var session = new RecordingSession(_target, _output, _log);
        session.Start();
        var digital = OtherFile("ScontrinoDigitale.txt");
        var content = Encoding.ASCII.GetBytes("QR DELLO SCONTRINO DIGITALE\r\n");
        File.WriteAllBytes(digital, content);
        WaitUntil(() => IndexLines(session).Any(l => l.Contains("\"tipo\":\"cartella\"") && l.Contains("\"nome\":\"ScontrinoDigitale.txt\"")), "notifica della cartella");
        session.Stop();   // di solito prima della pausa di 300 ms: la copia in attesa si fa subito

        using (var zip = ZipFile.OpenRead(session.ZipPath))
        {
            var entry = zip.Entries.Single(e => e.FullName.Replace('\\', '/').EndsWith("/altri/0001_ScontrinoDigitale.txt"));
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            Assert.Equal(content, memory.ToArray());
        }
        Assert.Contains("Copie di altri file: ScontrinoDigitale.txt 1", Summary(session));

        // Dopo Stop niente più copie né righe nell'indice, anche se il file cambia ancora.
        var lines = IndexLines(session).Count;
        File.WriteAllBytes(digital, Encoding.ASCII.GetBytes("DOPO LA FINE\r\n"));
        Thread.Sleep(500);
        Assert.Equal(lines, IndexLines(session).Count);
        Assert.Single(OtherCopies(session));
    }
}
