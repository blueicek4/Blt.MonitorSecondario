using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>L'Agent vero, su una porta libera: un display si collega via WebSocket e la cassa scrive il file.</summary>
public sealed class AgentAppTests : IAsyncLifetime
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "bt-agentapp-" + Guid.NewGuid().ToString("N"));
    WebApplication? _app;
    string _file = "";
    Uri _base = null!;

    public async Task InitializeAsync()
    {
        _file = Path.Combine(_root, "MonitorSecondario", "Scontrino.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        _app = AgentApp.Build([], builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Agent:FileCassa"] = _file,
            ["Agent:Porta"] = "0",
            ["Agent:CartellaLog"] = Path.Combine(_root, "log"),
            ["Agent:CartellaDisplay"] = Path.Combine(_root, "display-assente"),
        }));
        await _app.StartAsync();
        _base = new Uri(_app.Urls.First());
    }

    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static async Task<JsonObject> Receive(ClientWebSocket socket, Func<JsonObject, bool> wanted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var text = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, timeout.Token);
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);
            var json = JsonNode.Parse(text.ToString())!.AsObject();
            if (wanted(json))
                return json;
        }
    }

    [Fact]
    public async Task A_display_gets_the_state_then_every_change()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://{_base.Authority}/ws"), CancellationToken.None);
        var sync = await Receive(socket, _ => true);
        Assert.Equal("sync", (string)sync["t"]!);
        Assert.Equal("idle", (string)sync["state"]!["phase"]!);

        var temp = _file + ".tmp";
        await File.WriteAllTextAsync(temp, "SNF\r\n \r\n Caffè                    1,20\r\nTOT. DA PAGARE           1,20\r\n", new UTF8Encoding(false));
        File.Move(temp, _file);

        var line = await Receive(socket, m => (string)m["t"]! == "sale.line");
        Assert.Equal("Caffè", (string)line["line"]!["desc"]!);
        var total = await Receive(socket, m => (string)m["t"]! == "sale.total");
        Assert.Equal(120, (long)total["total"]!);
        await Receive(socket, m => (string)m["t"]! == "hb");   // il battito arriva anche senza novità
        Assert.False(File.Exists(_file));   // il file è stato tolto dopo la lettura
    }

    [Fact]
    public async Task Status_diagnostics_and_display_address()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = _base };
        var status = JsonNode.Parse(await http.GetStringAsync("/api/stato"))!;
        Assert.Equal("cassa", (string)status["modalita"]!);
        Assert.Equal(_file, (string)status["file"]!);

        Assert.Contains("Diagnostica Agent", await http.GetStringAsync("/diagnostica"));
        Assert.Contains("il display non è installato", await http.GetStringAsync("/"));

        var redirect = await http.GetAsync("/display");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/", redirect.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/ws")).StatusCode);
    }

    [Fact]
    public void Replay_reads_the_versions_of_a_recording_in_order()
    {
        var steps = Server.AgentEngine.LoadReplay(Samples.Folder("2026-10-20_064854"));
        Assert.Equal(28, steps.Count);
        Assert.EndsWith("0001_06-49-21.583.txt", steps[0].File);
        Assert.True(steps.Zip(steps.Skip(1)).All(p => p.First.Ms <= p.Second.Ms));
    }
}
