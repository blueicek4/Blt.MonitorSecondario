using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Bluetech.Display.Agent.Cassa;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

public sealed class CashFileSourceTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-agent-" + Guid.NewGuid().ToString("N"));
    readonly string _target;

    public CashFileSourceTests()
    {
        _target = Path.Combine(_root, "MonitorSecondario", "Scontrino.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_target)!);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Il contenuto compare tutto insieme, come una scrittura conclusa (file temporaneo spostato al suo posto).</summary>
    void WriteAtomically(string text)
    {
        var temp = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(temp, Encoding.ASCII.GetBytes(text));
        File.Move(temp, _target);
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

    [Fact]
    public void Each_version_is_delivered_once_and_then_removed()
    {
        var received = new ConcurrentQueue<string>();
        using var source = new CashFileSource(_target, TimeSpan.FromMinutes(5), NullLogger.Instance, b => received.Enqueue(Encoding.ASCII.GetString(b)));
        source.Start();

        for (var i = 1; i <= 3; i++)
        {
            var text = $"SNF\r\n ARTICOLO {i}   1,00\r\nTOT. DA PAGARE   {i},00\r\n";
            WriteAtomically(text);   // fallirebbe se il file precedente non fosse stato tolto
            WaitUntil(() => received.Count == i && !File.Exists(_target), "versione " + i);
            Assert.Equal(text, received.Last());
        }
        Assert.Equal(3, source.FilesRead);
        Assert.Equal(3, source.FilesRemoved);
    }

    [Fact]
    public void An_old_file_found_at_startup_is_removed_without_showing_it()
    {
        File.WriteAllText(_target, "SNF\r\n CONTO DI IERI   9,00\r\n");
        File.SetLastWriteTimeUtc(_target, DateTime.UtcNow.AddHours(-10));
        var received = new ConcurrentQueue<byte[]>();
        using var source = new CashFileSource(_target, TimeSpan.FromMinutes(5), NullLogger.Instance, received.Enqueue);
        source.Start();
        WaitUntil(() => !File.Exists(_target), "file vecchio tolto");
        Thread.Sleep(300);
        Assert.Empty(received);
    }

    [Fact]
    public void A_recent_file_found_at_startup_is_shown()
    {
        File.WriteAllText(_target, "SNF\r\n CAFFE   1,00\r\n");
        var received = new ConcurrentQueue<byte[]>();
        using var source = new CashFileSource(_target, TimeSpan.FromMinutes(5), NullLogger.Instance, received.Enqueue);
        source.Start();
        WaitUntil(() => received.Count == 1 && !File.Exists(_target), "file recente letto e tolto");
    }

    [Fact]
    public void Missing_folder_is_created_and_watched()
    {
        var target = Path.Combine(_root, "NonEsiste", "Scontrino.txt");
        var received = new ConcurrentQueue<byte[]>();
        using var source = new CashFileSource(target, TimeSpan.FromMinutes(5), NullLogger.Instance, received.Enqueue);
        source.Start();
        File.WriteAllText(target, "SNF\r\n CAFFE   1,00\r\n");
        WaitUntil(() => received.Count == 1, "file letto");
    }

    /// <summary>
    /// Un altro programma tiene il file aperto senza permettere di eliminarlo, più a lungo dei tentativi immediati. Su
    /// Windows la cancellazione fallisce: il file va tolto appena si libera, senza mostrarlo una seconda volta. Altrove
    /// la condivisione non è imposta e il file si toglie subito.
    /// </summary>
    [Fact]
    public void A_file_that_could_not_be_removed_is_removed_later_and_shown_once()
    {
        File.WriteAllText(_target, "SNF\r\n CAFFE   1,00\r\n");
        var received = new ConcurrentQueue<byte[]>();
        using var source = new CashFileSource(_target, TimeSpan.FromMinutes(5), NullLogger.Instance, received.Enqueue);
        using (new FileStream(_target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            source.Start();
            WaitUntil(() => received.Count == 1, "file letto");
            Thread.Sleep(1500);
        }
        WaitUntil(() => !File.Exists(_target), "file tolto quando si libera");
        Thread.Sleep(500);
        Assert.Single(received);
    }

    [Fact]
    public void An_old_file_that_cannot_be_removed_at_startup_is_never_shown()
    {
        File.WriteAllText(_target, "SNF\r\n CONTO DI IERI   9,00\r\n");
        File.SetLastWriteTimeUtc(_target, DateTime.UtcNow.AddHours(-10));
        var received = new ConcurrentQueue<byte[]>();
        using var source = new CashFileSource(_target, TimeSpan.FromMinutes(5), NullLogger.Instance, received.Enqueue);
        using (new FileStream(_target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            source.Start();
            Thread.Sleep(1500);
        }
        WaitUntil(() => !File.Exists(_target), "file vecchio tolto quando si libera");
        Thread.Sleep(500);
        Assert.Empty(received);
    }
}
