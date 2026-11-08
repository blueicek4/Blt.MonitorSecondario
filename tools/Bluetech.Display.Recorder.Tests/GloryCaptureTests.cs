using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

/// <summary>La cattura di rete verso la cassa automatica, con un pktmon simulato: i comandi giusti, nell'ordine giusto.</summary>
public sealed class GloryCaptureTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-recorder-glory-" + Guid.NewGuid().ToString("N"));
    readonly RecordingSession _session;
    readonly TestLog _log = new();
    readonly List<string> _calls = new();

    public GloryCaptureTests()
    {
        var target = Path.Combine(_root, "MonitorSecondario", "Scontrino.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        _session = new RecordingSession(target, Path.Combine(_root, "Registrazioni"), _log)
        {
            FindOldMonitors = () => new string[0],
            FindDisplayAgents = () => new string[0],
        };
        _session.Start();
    }

    public void Dispose()
    {
        _session.Stop();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Un pktmon finto: risponde con quello che dice il test e, a "stop", lascia il file come farebbe quello vero.</summary>
    GloryCapture Capture(bool modern = true, bool admin = true, bool available = true, Func<string, PktmonResult> answer = null) => new(_session, _log)
    {
        IsAdministrator = () => admin,
        Available = () => available,
        Run = args =>
        {
            _calls.Add(args);
            if (answer?.Invoke(args) is { } custom)
                return custom;
            if (args == "status")
                return new PktmonResult(0, "Packet Monitor is not running.");
            if (args == "start help")
                return new PktmonResult(0, modern ? "pktmon start [--capture [--counters-only]] [--pkt-size <bytes>] [--file-name <name>]" : "pktmon start [--etw] [-p size] [-f file]");
            if (args.StartsWith("start"))
                File.WriteAllBytes(Path.Combine(_session.SessionFolder, GloryCapture.EtlName), new byte[4096]);
            if (args.StartsWith("etl2pcap"))
                File.WriteAllBytes(Path.Combine(_session.SessionFolder, GloryCapture.PcapName), new byte[2048]);
            return new PktmonResult(0, "");
        },
    };

    [Fact]
    public void Only_a_lan_ipv4_address_is_accepted()
    {
        Assert.True(GloryCapture.IsLanAddress("192.168.1.50", out _));
        Assert.True(GloryCapture.IsLanAddress("10.0.0.7", out _));
        Assert.True(GloryCapture.IsLanAddress("172.16.4.2", out _));
        Assert.False(GloryCapture.IsLanAddress("8.8.8.8", out _));
        Assert.False(GloryCapture.IsLanAddress("172.32.0.1", out _));
        Assert.False(GloryCapture.IsLanAddress("192.168.1", out _));   // IPAddress.Parse lo accetterebbe
        Assert.False(GloryCapture.IsLanAddress("fe80::1", out _));
        Assert.False(GloryCapture.IsLanAddress("glory", out _));
    }

    [Fact]
    public void A_capture_filters_on_the_machine_starts_stops_first_and_converts_for_wireshark()
    {
        var glory = Capture();
        Assert.True(glory.Start("192.168.1.50"));
        Assert.True(glory.Running);
        glory.Stop();
        Assert.False(glory.Running);

        var etl = Path.Combine(_session.SessionFolder, GloryCapture.EtlName);
        var pcap = Path.Combine(_session.SessionFolder, GloryCapture.PcapName);
        Assert.Equal(new[]
        {
            "status",
            "start help",
            "filter remove",
            "filter add BluetechGlory -i 192.168.1.50",
            $"start --capture --comp nics --pkt-size 0 --file-name \"{etl}\"",
            "stop",
            "filter remove",
            $"etl2pcap \"{etl}\" --out \"{pcap}\"",
        }, _calls);
        Assert.Contains("rete-glory.pcapng per Wireshark", glory.Summary);
        Assert.Contains(File.ReadAllLines(Path.Combine(_session.SessionFolder, "eventi.jsonl")),
            l => l.Contains("\"tipo\":\"rete-glory\"") && l.Contains("\"azione\":\"fine\"") && l.Contains("\"pcapng\":true"));
    }

    [Fact]
    public void Without_administrator_or_pktmon_nothing_runs_and_the_operator_is_told_why()
    {
        Assert.False(Capture(admin: false).Start("192.168.1.50"));
        Assert.Contains("amministratore", _log.Lines.Last());
        Assert.False(Capture(available: false).Start("192.168.1.50"));
        Assert.Contains("1809", _log.Lines.Last());
        Assert.Empty(_calls);
    }

    [Fact]
    public void A_capture_already_running_is_left_alone()
    {
        var glory = Capture(answer: args => args == "status" ? new PktmonResult(0, "Packet Monitor is running.\nCollected data: 120 packets") : null);
        Assert.False(glory.Start("192.168.1.50"));
        Assert.Equal(new[] { "status" }, _calls);
        Assert.Contains("già registrando", glory.Summary);
    }

    [Fact]
    public void A_failed_start_removes_the_filter_again()
    {
        var glory = Capture(answer: args => args.StartsWith("start --") ? new PktmonResult(5, "Access is denied.") : null);
        Assert.False(glory.Start("192.168.1.50"));
        Assert.False(glory.Running);
        Assert.Equal("filter remove", _calls.Last());
        Assert.Contains("Access is denied.", glory.Summary);
        glory.Stop();   // niente da fermare
        Assert.DoesNotContain("stop", _calls);
    }

    [Fact]
    public void On_windows_1809_the_old_syntax_is_used_and_the_etl_is_kept()
    {
        var glory = Capture(modern: false);
        Assert.True(glory.Start("192.168.1.50"));
        glory.Stop();
        Assert.Contains(_calls, c => c.StartsWith("start --etw -p 0"));
        Assert.DoesNotContain(_calls, c => c.StartsWith("etl2pcap"));
        Assert.Contains("solo rete-glory.etl", glory.Summary);
    }

    [Theory]
    [InlineData("Packet Monitor is not running.", false)]
    [InlineData("", false)]
    [InlineData("Packet Monitor is running.", true)]
    public void Status_output_is_read(string output, bool running) => Assert.Equal(running, GloryCapture.LooksRunning(output));
}
