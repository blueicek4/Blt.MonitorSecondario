using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    List<string> IndexLines(RecordingSession session) =>
        File.ReadAllLines(Path.Combine(session.SessionFolder, "eventi.jsonl")).ToList();

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
        WaitUntil(() => Directory.GetFiles(session.SessionFolder, "0*.txt").Any(f => File.ReadAllBytes(f).SequenceEqual(final)), "ultima versione");
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
        Assert.DoesNotContain(File.ReadAllLines(Path.Combine(session.SessionFolder, "eventi.jsonl")), l => l.Contains("troppo tardi"));
    }

    sealed class TestLog : IRecorderLog
    {
        readonly List<string> _lines = new();

        public void Line(string text, ConsoleColor? color = null)
        {
            lock (_lines)
                _lines.Add(text);
        }
    }
}
