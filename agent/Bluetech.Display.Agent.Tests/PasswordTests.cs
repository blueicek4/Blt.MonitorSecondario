using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Impostazioni;
using Microsoft.AspNetCore.Http;
using Xunit;
using static Bluetech.Display.Agent.Tests.TestAgent;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// La password del back office: dal file dell'installazione o del tecnico (mai una password uguale per tutti), e il back
/// office che si apre solo dal PC dell'Agent finché non lo si apre alla rete.
/// </summary>
public sealed class PasswordTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "bt-password-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_folder, PasswordFile.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    SettingsStore StoreWith(string password)
    {
        var store = new SettingsStore(_folder);
        store.Update(s =>
        {
            s.Password = AdminAccess.Hash(password);
            return null;
        });
        return store;
    }

    static DefaultHttpContext From(string address, string? cookie = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (cookie != null)
            context.Request.Headers.Cookie = cookie;
        return context;
    }

    [Fact]
    public async Task A_password_left_in_the_file_replaces_the_old_one_and_the_file_goes_away()
    {
        var root = Path.Combine(_folder, "agent");
        Directory.CreateDirectory(Path.Combine(root, "dati"));
        var file = Path.Combine(root, "dati", PasswordFile.FileName);
        await File.WriteAllTextAsync(file, "﻿  la-password-del-tecnico \r\n");   // come la salva il Blocco note, con un a capo

        await using var agent = await Start(root: root, login: false);
        Assert.False(File.Exists(file));
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.Admin.PostAsJsonAsync("/api/bo/accesso", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/accesso", new { password = "la-password-del-tecnico" })).StatusCode);
        Assert.DoesNotContain("la-password-del-tecnico", await File.ReadAllTextAsync(Path.Combine(root, "dati", SettingsStore.FileName)));   // solo l'hash
        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("AVV") && ((string)l!).Contains("impostata con nuova-password.txt"));
    }

    [Fact]
    public void A_password_that_does_not_do_is_refused_but_never_left_on_the_disk()
    {
        var store = StoreWith("vecchia-password");
        Assert.Null(PasswordFile.Apply(_folder, store));   // senza file non succede niente

        File.WriteAllText(FilePath, "12345");
        var outcome = PasswordFile.Apply(_folder, store)!;
        Assert.True(outcome.Problem);
        Assert.Contains("almeno 6 caratteri", outcome.Note);
        Assert.False(File.Exists(FilePath));   // la password in chiaro non resta sul disco, anche se sbagliata
        Assert.True(AdminAccess.Verify(store.Current.Password!, "vecchia-password"));
    }

    [Fact]
    public void A_generated_password_is_readable_unique_and_valid_from_the_next_start()
    {
        var output = new StringWriter();
        var messages = new StringWriter();
        Assert.Equal(0, PasswordFile.GenerateCommand(_folder, output, messages));
        var password = output.ToString().Trim();
        Assert.Matches("^[a-z2-9]{4}(-[a-z2-9]{4}){3}$", password);
        Assert.DoesNotMatch("[01ilo]", password);   // niente caratteri che si confondono a dettarla
        Assert.Equal(password, File.ReadAllText(FilePath).Trim());
        Assert.Contains("riavvia il servizio", messages.ToString());
        Assert.DoesNotContain(password, messages.ToString());   // sull'uscita solo lei, per l'installazione

        var store = new SettingsStore(_folder);
        Assert.False(PasswordFile.Apply(_folder, store)!.Problem);
        Assert.True(AdminAccess.Verify(store.Current.Password!, password));
        Assert.Equal(100, Enumerable.Range(0, 100).Select(_ => PasswordFile.Generate()).Distinct().Count());
        Assert.Equal(Path.GetFullPath(_folder), AgentApp.DataFolder(["--dati", _folder]));   // la stessa cartella dell'Agent
    }

    [Fact]
    public void The_back_office_opens_only_on_the_agent_pc_until_it_is_opened_to_the_network()
    {
        var store = StoreWith("password-di-prova");
        var admin = new AdminAccess(store);

        Assert.False(admin.Reachable(From("192.168.1.20")));
        Assert.Equal(AdminAccess.OnlyFromThisPc, admin.Login(From("192.168.1.20"), "password-di-prova"));   // nemmeno con la password giusta
        Assert.True(admin.Reachable(From("::ffff:127.0.0.1")));
        Assert.Null(admin.Login(From("127.0.0.1"), "password-di-prova"));   // dal PC dell'Agent, anche in desktop remoto

        store.Update(s =>
        {
            s.BackOfficeDallaRete = true;
            return null;
        });
        var remote = From("192.168.1.20");
        Assert.Null(admin.Login(remote, "password-di-prova"));
        var token = remote.Response.Headers.SetCookie.ToString().Split(';')[0].Split('=', 2)[1];
        Assert.True(admin.IsAuthenticated(From("192.168.1.20", AdminAccess.Cookie + "=" + token)));

        store.Update(s =>
        {
            s.BackOfficeDallaRete = false;
            return null;
        });
        Assert.False(admin.IsAuthenticated(From("192.168.1.20", AdminAccess.Cookie + "=" + token)));   // la sessione dalla rete finisce subito

        // Non è un'impostazione di rete: vale subito, senza riavvio.
        var runtime = new Server.AgentRuntime { DataFolder = _folder, LogFolder = _folder, DisplayFolder = _folder, StartupNetwork = store.Current.Rete };
        store.Update(s =>
        {
            s.BackOfficeDallaRete = true;
            return null;
        });
        Assert.False(runtime.RestartNeeded(store.Current));
    }

    [Fact]
    public async Task The_option_is_in_the_back_office_and_its_change_is_in_the_log()
    {
        await using var agent = await Start();
        var session = await Json(await agent.Admin.GetAsync("/api/bo/sessione"));
        Assert.True((bool)session["raggiungibile"]!);
        var settings = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/impostazioni"))!;
        Assert.False((bool)settings["BackOfficeDallaRete"]!);

        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var options = new JsonObject
        {
            ["BackOfficeDallaRete"] = true,
            ["ChiusuraVenditaDopoSecondi"] = 60,
            ["ScontrinoDigitale"] = settings["ScontrinoDigitale"]!.DeepClone(),
            ["Rete"] = new JsonObject { ["Porta"] = port, ["ApertoInRete"] = false, ["Https"] = new JsonObject { ["Attivo"] = false, ["Porta"] = 5443, ["Certificato"] = "", ["PasswordCertificato"] = null } },
        };
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/opzioni", options)).StatusCode);
        Assert.True((bool)JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/impostazioni"))!["BackOfficeDallaRete"]!);
        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("Back office aperto anche alla rete"));
    }
}
