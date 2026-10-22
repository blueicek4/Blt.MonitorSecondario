using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// Un Agent vero su una porta libera, con le impostazioni in una cartella temporanea: il back office (già entrato con la
/// password) e i dispositivi abbinati con il codice, come in cassa.
/// </summary>
sealed class TestAgent : IAsyncDisposable
{
    public const string Password = "segreta-di-prova";
    public const string TechnicianPassword = "tecnico-di-prova";

    readonly CookieContainer _adminCookies = new();
    readonly bool _ownsRoot;

    TestAgent(string root, bool ownsRoot)
    {
        Root = root;
        _ownsRoot = ownsRoot;
    }

    public string Root { get; }
    public string DataFolder => Path.Combine(Root, "dati");
    public WebApplication App { get; private set; } = null!;
    public Uri Base { get; private set; } = null!;
    public HttpClient Admin { get; private set; } = null!;

    public string CashFile(string id = "cassa1") => Path.Combine(Root, id, "Scontrino.txt");

    /// <summary>
    /// Le impostazioni di partenza: la password del titolare (senza quella del tecnico vede anche l'installazione), un
    /// marchio, una cassa, la porta libera.
    /// </summary>
    public static AgentSettings Settings(string root) => new()
    {
        Installazione =
        {
            Accessi = [new AccessEntry { Ruolo = AccessEntry.Owner, Password = AdminAccess.Hash(Password) }],
            Rete = { Porta = 0 },
        },
        Marchi = [new BrandConfig { Id = "locale", Nome = "Il locale" }],
        Casse = [new CashPointConfig { Id = "cassa1", Nome = "Cassa 1", Marchio = "locale", FileCassa = Path.Combine(root, "cassa1", "Scontrino.txt") }],
    };

    /// <summary>Il marchio delle prove (il primo).</summary>
    public static BrandConfig Brand(AgentSettings settings) => settings.Marchi[0];

    /// <summary>Un'altra cassa, con il file nella sua cartella accanto a quella della prima.</summary>
    public static void AddCash(AgentSettings settings, string id, string name, string brand = "locale")
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(settings.Casse[0].FileCassa))!;
        settings.Casse.Add(new CashPointConfig { Id = id, Nome = name, Marchio = brand, FileCassa = Path.Combine(root, id, "Scontrino.txt") });
    }

    public static async Task<TestAgent> Start(Action<AgentSettings>? configure = null, string? root = null, Dictionary<string, string?>? options = null, bool writeSettings = true, bool login = true)
    {
        var agent = new TestAgent(root ?? Path.Combine(Path.GetTempPath(), "bt-agent-" + Guid.NewGuid().ToString("N")), root == null);
        Directory.CreateDirectory(agent.DataFolder);
        if (writeSettings)
        {
            var settings = Settings(agent.Root);
            configure?.Invoke(settings);
            await File.WriteAllTextAsync(Path.Combine(agent.DataFolder, SettingsStore.FileName), JsonSerializer.Serialize(settings, AgentSettings.Json));
        }
        await agent.Run(options);
        if (writeSettings && login)
            await agent.Login();
        return agent;
    }

    async Task Run(Dictionary<string, string?>? options)
    {
        var config = new Dictionary<string, string?>
        {
            ["Agent:CartellaDati"] = DataFolder,
            ["Agent:CartellaLog"] = Path.Combine(Root, "log"),
            ["Agent:CartellaDisplay"] = Path.Combine(Root, "display"),
        };
        foreach (var (key, value) in options ?? [])
            config[key] = value;
        App = AgentApp.Build([], builder => builder.Configuration.AddInMemoryCollection(config));
        await App.StartAsync();
        Base = new Uri(App.Urls.First(u => u.StartsWith("http:", StringComparison.Ordinal)));
        Admin = new HttpClient(new HttpClientHandler { CookieContainer = _adminCookies, AllowAutoRedirect = false }) { BaseAddress = Base };
    }

    public async Task Login(string password = Password)
    {
        var response = await Admin.PostAsJsonAsync("/api/bo/accesso", new { password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Uno schermo nuovo: apre /auth, il back office scrive il suo codice, lo schermo riceve il cookie.</summary>
    public async Task<Device> Pair(string name, string cashPoint = "cassa1", string? printer = null)
    {
        var device = NewBrowser();
        var start = await Json(await device.Http.PostAsync("/api/abbinamento", null));
        var approved = await Admin.PostAsJsonAsync("/api/bo/schermi", new { codice = (string)start["codice"]!, nome = name, cassa = cashPoint, stampante = printer });
        var answer = await Json(approved);
        Assert.True(approved.IsSuccessStatusCode, answer.ToJsonString());
        Assert.Equal("autorizzato", (string)(await Json(await device.Http.GetAsync("/api/abbinamento")))["stato"]!);
        return device with { Id = (string)answer["id"]! };
    }

    /// <summary>Un browser con i suoi cookie, non ancora autorizzato.</summary>
    public Device NewBrowser()
    {
        var cookies = new CookieContainer();
        return new Device(new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = Base }, cookies, "");
    }

    public async Task<ClientWebSocket> Connect(Device? device, string query = "", CookieContainer? cookies = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.Cookies = cookies ?? device?.Cookies;
        await socket.ConnectAsync(new Uri($"ws://{Base.Authority}/ws{query}"), CancellationToken.None);
        return socket;
    }

    public CookieContainer AdminCookies => _adminCookies;

    public async Task<JsonNode> Status() => JsonNode.Parse(await Admin.GetStringAsync("/api/bo/stato"))!;

    public static async Task<JsonObject> Json(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    /// <summary>Come Passepartout: scrive accanto e poi rinomina.</summary>
    public static async Task Write(string file, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, file);
    }

    public static Task Write(string file, string text) => Write(file, new UTF8Encoding(false).GetBytes(text));

    public static async Task<JsonObject> Receive(ClientWebSocket socket, Func<JsonObject, bool> wanted, int seconds = 10)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var text = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException($"chiuso dall'Agent: {result.CloseStatusDescription}");
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);
            var json = JsonNode.Parse(text.ToString())!.AsObject();
            if (wanted(json))
                return json;
        }
    }

    public static Func<JsonObject, bool> Type(string type) => m => (string)m["t"]! == type;

    public static async Task Send(ClientWebSocket socket, JsonObject message) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>Ferma l'Agent e lo riavvia con la stessa cartella (impostazioni, statistiche, certificato).</summary>
    public async Task Restart(Dictionary<string, string?>? options = null)
    {
        await StopApp();
        await Run(options);
    }

    async Task StopApp()
    {
        if (App == null)
            return;
        await App.StopAsync();
        await App.DisposeAsync();
        App = null!;
    }

    public async ValueTask DisposeAsync()
    {
        await StopApp();
        if (!_ownsRoot)
            return;
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

sealed record Device(HttpClient Http, CookieContainer Cookies, string Id);
