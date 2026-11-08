using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

/// <summary>Il percorso dei flussi di pagamento e le foto dello schermo, con una fotocamera finta.</summary>
public sealed class FlowsAndPhotosTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-recorder-flussi-" + Guid.NewGuid().ToString("N"));
    readonly string _target;
    readonly TestLog _log = new();

    public FlowsAndPhotosTests()
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
        Assert.True(PaymentFlow.Find(3).Glory);
        Assert.Single(PaymentFlow.All, f => f.Glory);
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

    /// <summary>Fotocamera finta: ogni foto è quella che il test ha messo in coda (o null se lo schermo non si legge).</summary>
    sealed class FakeCamera : IScreenCamera
    {
        public readonly Queue<ScreenShot> Next = new();
        public int Taken;

        public ScreenShot Take()
        {
            Taken++;
            return Next.Count > 0 ? Next.Dequeue() : null;
        }

        public static ScreenShot Shot(byte cell, int size = 100) => new()
        {
            Jpeg = Enumerable.Repeat((byte)0xFF, size).ToArray(),
            Fingerprint = Enumerable.Repeat(cell, 96 * 54).ToArray(),
            Width = 1920,
            Height = 1080,
        };
    }

    [Fact]
    public void Photos_are_saved_only_when_the_screen_changes_and_a_version_points_to_the_last_one()
    {
        using var session = NewSession();
        session.Start();
        var camera = new FakeCamera();
        var recorder = new ScreenRecorder(camera, session, _log, Timeout.Infinite);   // le foto le comanda il test
        Assert.True(recorder.SetOn(true));
        camera.Next.Enqueue(FakeCamera.Shot(10));
        recorder.Capture("prova", onlyIfChanged: true);
        camera.Next.Enqueue(FakeCamera.Shot(12));   // sotto la soglia: uguale
        recorder.Capture("ogni secondo", onlyIfChanged: true);
        camera.Next.Enqueue(FakeCamera.Shot(12));
        recorder.Capture("versione #0001", onlyIfChanged: false);
        camera.Next.Enqueue(FakeCamera.Shot(40));   // cambiata
        recorder.Capture("ogni secondo", onlyIfChanged: true);
        recorder.Stop();
        session.Stop();

        Assert.Equal(2, recorder.Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(session.SessionFolder, "foto"), "*.jpg").Length);
        var index = Index(session);
        Assert.Contains(index, l => l.Contains("\"tipo\":\"foto\"") && l.Contains("versione #0001") && l.Contains("\"schermoUguale\":true") && l.Contains("0001_"));
        Assert.Equal(2, index.Count(l => l.Contains("\"tipo\":\"foto\"") && l.Contains("\"byte\":100")));
    }

    [Fact]
    public void A_screen_that_cannot_be_read_turns_the_photos_off_with_the_reason()
    {
        using var session = NewSession();
        session.Start();
        var recorder = new ScreenRecorder(new FakeCamera(), session, _log, Timeout.Infinite);
        recorder.SetOn(true);
        for (var i = 0; i < 3; i++)
            recorder.Capture("ogni secondo", onlyIfChanged: true);   // tre volte di fila lo schermo non si legge
        Assert.False(recorder.On);
        Assert.False(recorder.SetOn(true));
        Assert.Contains("non si legge", recorder.DisabledReason);
        recorder.Stop();
        session.Stop();
        Assert.Contains(Index(session), l => l.Contains("\"tipo\":\"foto-schermo\"") && l.Contains("non si legge"));
    }

    [Fact]
    public void Without_a_camera_photos_say_why_and_stay_off()
    {
        using var session = NewSession();
        session.Start();
        var recorder = new ScreenRecorder(null, session, _log);
        Assert.False(recorder.SetOn(true));
        Assert.Contains("Windows", recorder.DisabledReason);
        recorder.Stop();
        session.Stop();
    }

    [Fact]
    public void One_changed_cell_is_a_change()
    {
        var a = new byte[96 * 54];
        var b = (byte[])a.Clone();
        Assert.False(ScreenRecorder.Differs(a, b));
        b[1234] = ScreenRecorder.ChangeThreshold - 1;
        Assert.False(ScreenRecorder.Differs(a, b));
        b[1234] = ScreenRecorder.ChangeThreshold;
        Assert.True(ScreenRecorder.Differs(a, b));
        Assert.True(ScreenRecorder.Differs(a, null));
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
