using System.Diagnostics;
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
        var placeholder = await http.GetStringAsync("/");
        Assert.Contains("il display non è installato", placeholder);
        Assert.Contains("http-equiv=\"refresh\"", placeholder);   // il Player la ricarica finché il display non c'è

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

    [Fact]
    public async Task The_websocket_refuses_pages_from_other_sites()
    {
        using var evil = new ClientWebSocket();
        evil.Options.SetRequestHeader("Origin", "https://evil.example");
        await Assert.ThrowsAsync<WebSocketException>(() => evil.ConnectAsync(new Uri($"ws://{_base.Authority}/ws"), CancellationToken.None));

        using var page = new ClientWebSocket();   // la pagina servita dall'Agent stesso
        page.Options.SetRequestHeader("Origin", $"http://{_base.Authority}");
        await page.ConnectAsync(new Uri($"ws://{_base.Authority}/ws"), CancellationToken.None);
        Assert.Equal("sync", (string)(await Receive(page, _ => true))["t"]!);
    }

    [Fact]
    public async Task A_display_copied_while_running_is_served_and_always_revalidated()
    {
        var folder = Path.Combine(_root, "display-assente");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "<!doctype html><title>display v2</title>");
        await File.WriteAllTextAsync(Path.Combine(folder, "assets", "index-abc123.js"), "console.log(2)");

        using var http = new HttpClient { BaseAddress = _base };
        var page = await http.GetAsync("/");
        Assert.Contains("display v2", await page.Content.ReadAsStringAsync());
        Assert.True(page.Headers.CacheControl!.NoCache);
        var script = await http.GetAsync("/assets/index-abc123.js");
        Assert.Equal(TimeSpan.FromDays(365), script.Headers.CacheControl!.MaxAge);
    }

    [Fact]
    public async Task Stopping_with_a_display_connected_is_quick_and_closes_cleanly()
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://{_base.Authority}/ws"), CancellationToken.None);
        await Receive(socket, _ => true);

        // Come un browser: legge sempre e risponde alla chiusura.
        var closed = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new byte[64 * 1024];
            WebSocketReceiveResult result;
            do
                result = await socket.ReceiveAsync(buffer, timeout.Token);
            while (result.MessageType != WebSocketMessageType.Close);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
            return result.CloseStatus;
        });

        var watch = Stopwatch.StartNew();
        var app = _app!;
        _app = null;
        await app.StopAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"arresto in {watch.Elapsed}");
        await app.DisposeAsync();
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await closed);
    }

    static async Task<(WebApplication App, HttpClient Http)> StartAgent(string root, Dictionary<string, string?> settings)
    {
        settings["Agent:Porta"] = "0";
        settings["Agent:CartellaLog"] = Path.Combine(root, "log");
        settings["Agent:CartellaDisplay"] = Path.Combine(root, "display");
        var app = AgentApp.Build([], builder => builder.Configuration.AddInMemoryCollection(settings));
        await app.StartAsync();
        return (app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    static async Task<string?> Problem(HttpClient http) => (string?)JsonNode.Parse(await http.GetStringAsync("/api/stato"))!["problema"];

    [Fact]
    public async Task A_folder_that_cannot_be_watched_keeps_the_agent_up_and_is_retried()
    {
        var root = Path.Combine(_root, "secondo");
        Directory.CreateDirectory(root);
        var blocker = Path.Combine(root, "MonitorSecondario");
        await File.WriteAllTextAsync(blocker, "un file al posto della cartella");
        var (app, http) = await StartAgent(root, new() { ["Agent:FileCassa"] = Path.Combine(blocker, "Scontrino.txt") });
        try
        {
            var watch = Stopwatch.StartNew();
            string? problem;
            while ((problem = await Problem(http)) == null && watch.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);
            Assert.Contains("Non riesco a sorvegliare", problem);
            Assert.Contains("Diagnostica Agent", await http.GetStringAsync("/diagnostica"));   // il servizio è su

            File.Delete(blocker);   // il percorso torna valido: al prossimo tentativo (10 s) parte e crea la cartella
            while (((problem = await Problem(http)) ?? "").Contains("Non riesco a sorvegliare") && watch.Elapsed < TimeSpan.FromSeconds(25))
                await Task.Delay(200);
            Assert.DoesNotContain("Non riesco a sorvegliare", problem ?? "");
            Assert.True(Directory.Exists(blocker));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_missing_replay_folder_is_reported()
    {
        var root = Path.Combine(_root, "terzo");
        var (app, http) = await StartAgent(root, new() { ["Agent:Replay"] = Path.Combine(root, "non-esiste") });
        try
        {
            var watch = Stopwatch.StartNew();
            string? problem;
            while ((problem = await Problem(http)) == null && watch.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);
            Assert.Contains("non esiste", problem);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
