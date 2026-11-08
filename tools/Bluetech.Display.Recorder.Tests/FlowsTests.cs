using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

/// <summary>Il percorso dei flussi di pagamento e quello che lascia nell'indice.</summary>
public sealed class FlowsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-recorder-flussi-" + Guid.NewGuid().ToString("N"));
    readonly string _target;
    readonly TestLog _log = new();

    public FlowsTests()
    {
        _target = Path.Combine(_root, "MonitorSecondario", "Scontrino.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_target));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    RecordingSession NewSession() => new(_target, Path.Combine(_root, "Registrazioni"), _log)
    {
        FindOldMonitors = () => new string[0],
        FindDisplayAgents = () => new string[0],
    };

    static string[] Index(RecordingSession session) =>
        File.ReadAllLines(Path.Combine(session.SessionFolder, "eventi.jsonl"));

    [Fact]
    public void The_six_flows_are_the_ones_asked_for_with_steps()
    {
        Assert.Equal(6, PaymentFlow.All.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, PaymentFlow.All.Select(f => f.Number));
        Assert.Contains("Glory", PaymentFlow.Find(3).Name);
        Assert.Contains("Satispay", PaymentFlow.Find(4).Name);
        Assert.Contains("digitale", PaymentFlow.Find(5).Name);
        Assert.Contains("Annullo", PaymentFlow.Find(6).Name);
        Assert.All(PaymentFlow.All, f => Assert.True(f.Steps.Count >= 3));
        Assert.Null(PaymentFlow.Find(0));
        Assert.Null(PaymentFlow.Find(7));
    }

    [Fact]
    public void The_guide_walks_the_steps_and_remembers_what_is_done()
    {
        var guide = new FlowGuide();
        Assert.Equal("1 · 2 · 3 · 4 · 5 · 6", guide.Progress());

        guide.Begin(PaymentFlow.Find(6));
        Assert.Equal("1 · 2 · 3 · 4 · 5 · 6 (in corso)", guide.Progress());
        while (guide.Advance()) { }
        Assert.True(guide.StepsFinished);
        Assert.False(guide.Advance());
        Assert.Equal(6, guide.End(out var complete).Number);
        Assert.True(complete);

        guide.Begin(PaymentFlow.Find(3));
        guide.Advance();
        var interrupted = guide.Begin(PaymentFlow.Find(4));   // un flusso nuovo prima di finire il precedente
        Assert.Equal(3, interrupted.Number);
        guide.End(out complete);
        Assert.False(complete);
        Assert.Equal("1 · 2 · 3 (interrotto) · 4 (interrotto) · 5 · 6 ✓", guide.Progress());
        Assert.Null(guide.Current);
    }

    [Fact]
    public void Versions_and_notes_carry_the_flow_they_belong_to()
    {
        using var session = NewSession();
        session.Start();
        session.CurrentFlow = 3;
        File.WriteAllBytes(_target, Encoding.ASCII.GetBytes("SNF\r\n \r\n CAFFE'                 1,10\r\nTOT. DA PAGARE        1,10\r\n"));
        WaitUntil(() => session.Versions == 1);
        session.AddNote("inserisco 5 euro");
        session.WriteEvent(session.NewEvent("passo").With("numero", 1));
        session.CurrentFlow = 0;
        session.AddNote("fuori dal flusso");
        session.AddSummary("Flussi: 1 · 2 · 3 ✓");
        session.Stop();

        var index = Index(session);
        Assert.Contains(index, l => l.Contains("\"tipo\":\"versione\"") && l.Contains("\"flusso\":3"));
        Assert.Contains(index, l => l.Contains("inserisco 5 euro") && l.Contains("\"flusso\":3"));
        Assert.Contains(index, l => l.Contains("\"tipo\":\"passo\"") && l.Contains("\"flusso\":3"));
        Assert.Contains(index, l => l.Contains("fuori dal flusso") && !l.Contains("\"flusso\""));
        Assert.Contains("Flussi: 1 · 2 · 3 ✓", File.ReadAllText(Path.Combine(session.SessionFolder, "riepilogo.txt")));
        // Dopo la chiusura l'indice non si tocca più.
        session.WriteEvent(session.NewEvent("passo"));
        Assert.Equal(index.Length, Index(session).Length);
    }

    [Fact]
    public void A_display_agent_found_at_start_ends_in_the_index_and_the_session_info()
    {
        using var session = new RecordingSession(_target, Path.Combine(_root, "Registrazioni"), _log)
        {
            FindOldMonitors = () => new string[0],
            FindDisplayAgents = () => new[] { @"C:\Bluetech\Agent\BluetechDisplayAgent.exe" },
        };
        session.Start();
        session.Stop();
        Assert.Contains(Index(session), l => l.Contains("\"tipo\":\"inizio\"") && l.Contains("BluetechDisplayAgent.exe"));
        Assert.Contains("Display Agent aperto: C:\\Bluetech\\Agent\\BluetechDisplayAgent.exe", File.ReadAllText(Path.Combine(session.SessionFolder, "sessione.txt")));
    }

    static void WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condizione non raggiunta");
            Thread.Sleep(20);
        }
    }
}
