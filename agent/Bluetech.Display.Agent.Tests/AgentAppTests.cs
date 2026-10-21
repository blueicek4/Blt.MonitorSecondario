using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Impostazioni;
using Xunit;
using static Bluetech.Display.Agent.Tests.TestAgent;

namespace Bluetech.Display.Agent.Tests;

/// <summary>L'Agent vero, su una porta libera: i display autorizzati si collegano via WebSocket e la cassa scrive il file.</summary>
public sealed class AgentAppTests
{
    const string Coffee = "SNF\r\n \r\n Caffè                    1,20\r\nTOT. DA PAGARE           1,20\r\n";
    const string Water = "SNF\r\n \r\n Acqua                    1,00\r\nTOT. DA PAGARE           1,00\r\n";

    [Fact]
    public async Task A_paired_display_gets_the_state_then_every_change()
    {
        await using var agent = await TestAgent.Start();
        var device = await agent.Pair("Player cassa");
        using var socket = await agent.Connect(device);
        var layout = await Receive(socket, _ => true);   // prima la disposizione, poi l'aspetto, poi il conto
        Assert.Equal("layout", (string)layout["t"]!);
        Assert.Null(layout["force"]);   // automatica: la sceglie lo schermo
        var look = await Receive(socket, _ => true);
        Assert.Equal("cfg", (string)look["t"]!);
        Assert.Equal("bluetech", (string)look["preset"]!);
        Assert.Equal("auto", (string)look["theme"]!["mode"]!);
        var sync = await Receive(socket, _ => true);
        Assert.Equal("sync", (string)sync["t"]!);
        Assert.Equal("idle", (string)sync["state"]!["phase"]!);

        await Write(agent.CashFile(), Coffee);
        var line = await Receive(socket, Type("sale.line"));
        Assert.Equal("Caffè", (string)line["line"]!["desc"]!);
        Assert.Equal(120, (long)(await Receive(socket, Type("sale.total")))["total"]!);
        await Receive(socket, Type("hb"));   // il battito arriva anche senza novità
        Assert.False(File.Exists(agent.CashFile()));   // il file è stato tolto dopo la lettura
    }

    [Fact]
    public async Task Without_authorization_the_customer_page_is_only_an_error()
    {
        await using var agent = await TestAgent.Start(login: false);
        var stranger = agent.NewBrowser();

        var page = await stranger.Http.GetAsync("/cliente");
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        var text = await page.Content.ReadAsStringAsync();
        Assert.Contains("Dispositivo non autorizzato", text);
        foreach (var hint in new[] { "/auth", "codice", "back office", "backoffice", "abbina" })
            Assert.DoesNotContain(hint, text, StringComparison.OrdinalIgnoreCase);   // nessuna indicazione su come entrare

        Assert.Equal("/cliente", (await stranger.Http.GetAsync("/")).Headers.Location!.OriginalString);
        Assert.Equal("/cliente", (await stranger.Http.GetAsync("/display")).Headers.Location!.OriginalString);
        Assert.Contains("Codice di abbinamento", await stranger.Http.GetStringAsync("/auth"));
        Assert.Contains("Back office", await stranger.Http.GetStringAsync("/backoffice"));
        Assert.Equal("/backoffice", (await stranger.Http.GetAsync("/diagnostica")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Http.GetAsync("/api/stato")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Http.GetAsync("/api/bo/impostazioni")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.Http.GetAsync("/ws")).StatusCode);
        await Assert.ThrowsAsync<WebSocketException>(() => agent.Connect(stranger));
    }

    [Fact]
    public async Task The_customer_page_can_be_installed_as_an_app()
    {
        await using var agent = await TestAgent.Start();
        var stranger = agent.NewBrowser();
        var manifest = JsonNode.Parse(await stranger.Http.GetStringAsync("/manifest.webmanifest"))!;
        Assert.Equal("/auth", (string)manifest["start_url"]!);   // installata: autorizzata va su /cliente, altrimenti il codice
        Assert.Equal("fullscreen", (string)manifest["display"]!);
        var icons = manifest["icons"]!.AsArray();
        Assert.Contains(icons, i => (string)i!["sizes"]! == "192x192");
        Assert.Contains(icons, i => (string)i!["sizes"]! == "512x512" && (string)i["purpose"]! == "maskable");
        foreach (var icon in icons)
        {
            var png = await stranger.Http.GetByteArrayAsync((string)icon!["src"]!);
            var size = int.Parse(((string)icon["sizes"]!).Split('x')[0]);
            Assert.Equal(size, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));   // la larghezza nell'IHDR
        }
        Assert.Equal(HttpStatusCode.OK, (await stranger.Http.GetAsync("/pwa/icona-180.png")).StatusCode);
        Assert.Contains("offline.html", await stranger.Http.GetStringAsync("/sw.js"));
        Assert.Contains("Mi ricollego", await stranger.Http.GetStringAsync("/pwa/offline.html"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync("/pwa/altro.js")).StatusCode);
        Assert.DoesNotContain("manifest", await (await stranger.Http.GetAsync("/cliente")).Content.ReadAsStringAsync());   // l'errore resta muto

        var device = await agent.Pair("Palmare");
        var page = await device.Http.GetStringAsync("/cliente");
        Assert.Contains("<link rel=\"manifest\" href=\"/manifest.webmanifest\">", page);
        Assert.Contains("/pwa/pwa.js", page);
        Assert.True(page.IndexOf("rel=\"manifest\"", StringComparison.Ordinal) < page.IndexOf("</head>", StringComparison.Ordinal));
        Assert.DoesNotContain("rel=\"manifest\"", await agent.Admin.GetStringAsync("/diagnostica"));
    }

    [Fact]
    public async Task A_device_is_authorized_with_the_code_typed_in_the_back_office()
    {
        await using var agent = await TestAgent.Start();
        var device = agent.NewBrowser();
        var first = await Json(await device.Http.PostAsync("/api/abbinamento", null));
        var code = (string)first["codice"]!;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(code, (string)(await Json(await device.Http.PostAsync("/api/abbinamento", null)))["codice"]!);   // ricaricando, lo stesso codice
        Assert.Equal("attesa", (string)(await Json(await device.Http.GetAsync("/api/abbinamento")))["stato"]!);

        var wrong = await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = code == "000000" ? "000001" : "000000", nome = "Palmare", cassa = "cassa1" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Codice non valido", (string)(await Json(wrong))["errore"]!);

        var spaced = code[..3] + " " + code[3..];   // come lo mostra lo schermo
        var approved = await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = spaced, nome = "Palmare sala", cassa = "cassa1" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        // La pagina ricaricata proprio adesso non chiede un codice nuovo: riceve l'autorizzazione.
        var poll = await device.Http.PostAsync("/api/abbinamento", null);
        Assert.Equal("autorizzato", (string)(await Json(poll))["stato"]!);
        Assert.Contains(poll.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bluetech_dispositivo=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Il tuo scontrino digitale", await device.Http.GetStringAsync("/cliente"));
        Assert.Equal("/cliente", (await device.Http.GetAsync("/auth")).Headers.Location!.OriginalString);

        // Nel file c'è l'hash del token, non il token del cookie.
        var token = device.Cookies.GetCookies(agent.Base)["bluetech_dispositivo"]!.Value;
        var saved = await File.ReadAllTextAsync(Path.Combine(agent.DataFolder, SettingsStore.FileName));
        Assert.DoesNotContain(token, saved);
        Assert.Contains(Accesso.AdminAccess.Sha256(token), saved);
        Assert.Contains("Palmare sala", saved);

        // Il codice vale una volta sola.
        var again = await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = code, nome = "Altro", cassa = "cassa1" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Each_display_sees_only_its_cash_point()
    {
        await using var agent = await TestAgent.Start(s => AddCash(s, "cassa2", "Cassa bar"));
        var room = await agent.Pair("Schermo sala", "cassa1");
        var bar = await agent.Pair("Schermo bar", "cassa2");
        using var roomSocket = await agent.Connect(room);
        using var barSocket = await agent.Connect(bar);
        await Receive(roomSocket, Type("sync"));
        await Receive(barSocket, Type("sync"));

        await Write(agent.CashFile("cassa1"), Coffee);
        Assert.Equal("Caffè", (string)(await Receive(roomSocket, Type("sale.line")))["line"]!["desc"]!);
        await Write(agent.CashFile("cassa2"), Water);
        Assert.Equal("Acqua", (string)(await Receive(barSocket, Type("sale.line")))["line"]!["desc"]!);   // il caffè della sala non è mai arrivato al bar
        Assert.Equal(100, (long)(await Receive(barSocket, Type("sale.total")))["total"]!);

        var status = await agent.Status();
        Assert.Equal(["cassa1", "cassa2"], status["casse"]!.AsArray().Select(c => (string)c!["id"]!).ToArray());
        Assert.All(status["casse"]!.AsArray(), c => Assert.Equal("sale", (string)c!["fase"]!));
        Assert.Equal(2, status["dispositivi"]!.AsArray().Count(d => (bool)d!["collegato"]!));
    }

    [Fact]
    public async Task Moving_or_revoking_a_device_takes_effect_on_its_display_at_once()
    {
        await using var agent = await TestAgent.Start(s => AddCash(s, "cassa2", "Cassa bar"));
        var device = await agent.Pair("Tablet", "cassa1");
        var socket = await agent.Connect(device);
        await Receive(socket, Type("sync"));

        var moved = await agent.Admin.PutAsJsonAsync($"/api/bo/dispositivi/{device.Id}", new { nome = "Tablet", cassa = "cassa2" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await Assert.ThrowsAsync<WebSocketException>(() => Receive(socket, _ => false));   // staccato: si ricollega
        socket.Dispose();
        socket = await agent.Connect(device);
        await Receive(socket, Type("sync"));
        await Write(agent.CashFile("cassa2"), Water);
        Assert.Equal("Acqua", (string)(await Receive(socket, Type("sale.line")))["line"]!["desc"]!);

        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync($"/api/bo/dispositivi/{device.Id}")).StatusCode);
        await Assert.ThrowsAsync<WebSocketException>(() => Receive(socket, _ => false));
        socket.Dispose();
        await Assert.ThrowsAsync<WebSocketException>(() => agent.Connect(device));
        Assert.Equal(HttpStatusCode.Forbidden, (await device.Http.GetAsync("/cliente")).StatusCode);
    }

    [Fact]
    public async Task The_paper_copy_prints_on_the_printer_of_the_device_that_asked()
    {
        var printer = new TcpListener(IPAddress.Loopback, 0);
        printer.Start();
        try
        {
            await using var agent = await TestAgent.Start(s =>
            {
                s.ScontrinoDigitale.MostraScontrino = true;
                s.ScontrinoDigitale.CopiaCartacea = true;
                s.Stampanti.Add(new PrinterConfig { Id = "stampante1", Nome = "Cassa", Indirizzo = $"127.0.0.1:{((IPEndPoint)printer.LocalEndpoint).Port}", TestoRitiro = "Ritira la copia al banco" });
            });
            var counter = await agent.Pair("Palmare con stampante", printer: "stampante1");
            var plain = await agent.Pair("Schermo senza stampante");
            using var counterSocket = await agent.Connect(counter);
            using var plainSocket = await agent.Connect(plain);
            await Receive(counterSocket, Type("sync"));
            await Receive(plainSocket, Type("sync"));

            await Write(agent.CashFile(), Samples.Receipt("scontrino-bar-pagato-carta.txt"));
            await Receive(counterSocket, Type("sale.close"));
            while (File.Exists(agent.CashFile()))
                await Task.Delay(20);
            await Write(agent.CashFile(), Samples.Receipt("qr-scontrino-digitale-prova.txt"));

            var digital = await Receive(counterSocket, Type("receipt.digital"));
            Assert.True((bool)digital["paperCopy"]!);
            Assert.Equal(120, (long)digital["receipt"]!["total"]!);
            Assert.False((bool)(await Receive(plainSocket, Type("receipt.digital")))["paperCopy"]!);   // niente pulsante senza stampante
            var id = (string)digital["id"]!;

            var printed = ReceiveAll(printer);
            await Send(counterSocket, new JsonObject { ["t"] = "receipt.paper", ["id"] = id });
            Assert.Equal("printing", (string)(await Receive(counterSocket, Type("receipt.paper.status")))["state"]!);
            var done = await Receive(counterSocket, Type("receipt.paper.status"));
            Assert.Equal("requested", (string)done["state"]!);
            Assert.Equal("Ritira la copia al banco", (string)done["message"]!);
            Assert.Equal("requested", (string)(await Receive(plainSocket, m => (string)m["t"]! == "receipt.paper.status" && (string)m["state"]! == "requested"))["state"]!);
            var bytes = await printed;
            Assert.Equal(new byte[] { 0x1B, 0x40 }, bytes[..2]);
            Assert.Contains("LOCALE 1", Encoding.Latin1.GetString(bytes));

            await Send(plainSocket, new JsonObject { ["t"] = "receipt.paper", ["id"] = id });
            var refused = await Receive(plainSocket, m => (string)m["t"]! == "receipt.paper.status" && (string)m["state"]! == "failed");
            Assert.Contains("non ha una stampante", (string)refused["message"]!);

            await Task.Delay(2100);   // un comando ogni 2 s
            await Send(counterSocket, new JsonObject { ["t"] = "receipt.paper", ["id"] = id });
            Assert.Equal("already", (string)(await Receive(counterSocket, Type("receipt.paper.status")))["state"]!);

            var status = await agent.Status();
            var copy = status["copieCartacee"]![0]!;
            Assert.Equal("inviata", (string)copy["esito"]!);
            Assert.Equal("Palmare con stampante", (string)copy["dispositivo"]!);
            Assert.Equal("Cassa", (string)copy["stampante"]!);

            var stats = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/statistiche?giorni=1"))!;
            var today = stats["giorni"]![0]!["casse"]!["cassa1"]!;
            Assert.Equal(1, (long)today["Vendite"]!);
            Assert.Equal(1, (long)today["Pagate"]!);
            Assert.Equal(120, (long)today["Incasso"]!);
            Assert.Equal(1, (long)today["ScontriniDigitali"]!);
            Assert.Equal(1, (long)today["CopieCartacee"]!);
            Assert.Equal(2, (long)today["File"]!);
        }
        finally
        {
            printer.Stop();
        }
    }

    [Fact]
    public async Task The_back_office_password_is_set_from_this_pc_then_needed()
    {
        await using var agent = await TestAgent.Start(s => s.Password = null, login: false);
        var session = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/sessione"))!;
        Assert.False((bool)session["passwordImpostata"]!);
        Assert.True((bool)session["locale"]!);

        Assert.Equal(HttpStatusCode.BadRequest, (await agent.Admin.PostAsJsonAsync("/api/bo/password", new { nuova = "corta" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/password", new { nuova = "la-mia-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.GetAsync("/api/bo/impostazioni")).StatusCode);   // già dentro
        var saved = await File.ReadAllTextAsync(Path.Combine(agent.DataFolder, SettingsStore.FileName));
        Assert.DoesNotContain("la-mia-password", saved);

        // Una seconda "prima password" non passa; cambiarla chiede quella attuale.
        var stranger = agent.NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.Http.PostAsJsonAsync("/api/bo/password", new { nuova = "presa-io-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.Admin.PostAsJsonAsync("/api/bo/password", new { attuale = "sbagliata", nuova = "un-altra-123" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsync("/api/bo/esci", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.Admin.GetAsync("/api/bo/stato")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.Admin.PostAsJsonAsync("/api/bo/accesso", new { password = "sbagliata" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/accesso", new { password = "la-mia-password" })).StatusCode);

        for (var i = 0; i < 5; i++)
            await stranger.Http.PostAsJsonAsync("/api/bo/accesso", new { password = "tentativo" + i });
        var locked = await stranger.Http.PostAsJsonAsync("/api/bo/accesso", new { password = "la-mia-password" });
        Assert.Equal((HttpStatusCode)429, locked.StatusCode);   // anche quella giusta, per un minuto
    }

    [Fact]
    public async Task Parallel_wrong_passwords_get_five_tries_at_most()
    {
        await using var agent = await TestAgent.Start(login: false);
        var attempts = Enumerable.Range(0, 30).Select(i => agent.NewBrowser().Http.PostAsJsonAsync("/api/bo/accesso", new { password = "sbagliata" + i }));
        var answers = await Task.WhenAll(attempts);
        Assert.True(answers.Count(a => a.StatusCode == HttpStatusCode.Unauthorized) <= 5, string.Join(",", answers.Select(a => (int)a.StatusCode)));
        Assert.Contains(answers, a => a.StatusCode == (HttpStatusCode)429);
    }

    [Fact]
    public async Task The_device_cookie_is_renewed_on_every_visit()
    {
        await using var agent = await TestAgent.Start();
        var device = await agent.Pair("Palmare");
        foreach (var path in new[] { "/cliente", "/api/abbinamento" })
        {
            var visit = await device.Http.GetAsync(path);
            var cookie = Assert.Single(visit.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bluetech_dispositivo=", StringComparison.Ordinal));
            var expires = DateTimeOffset.Parse(cookie.Split("expires=", StringSplitOptions.None)[1].Split(';')[0]);
            Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(399), TimeSpan.FromDays(401));   // il limite di Chrome, rinnovato
        }
    }

    [Fact]
    public async Task An_approved_code_survives_a_flood_of_new_codes()
    {
        await using var agent = await TestAgent.Start();
        var device = agent.NewBrowser();
        var code = (string)(await Json(await device.Http.PostAsync("/api/abbinamento", null)))["codice"]!;
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = code, nome = "Palmare", cassa = "cassa1" })).StatusCode);
        for (var i = 0; i < 60; i++)   // qualcuno chiede codici a raffica dallo stesso indirizzo
            await agent.NewBrowser().Http.PostAsync("/api/abbinamento", null);
        Assert.Equal("autorizzato", (string)(await Json(await device.Http.GetAsync("/api/abbinamento")))["stato"]!);
    }

    [Fact]
    public void A_port_in_use_is_seen_before_starting()
    {
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            Assert.False(AgentApp.PortAvailable(((IPEndPoint)busy.LocalEndpoint).Port, open: false));
        }
        finally
        {
            busy.Stop();
        }
        var ipv4Only = new TcpListener(IPAddress.Any, 0);   // su Windows il socket IPv6 "dual mode" non lo vede
        ipv4Only.Start();
        try
        {
            Assert.False(AgentApp.PortAvailable(((IPEndPoint)ipv4Only.LocalEndpoint).Port, open: true));
        }
        finally
        {
            ipv4Only.Stop();
        }
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var free = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Assert.True(AgentApp.PortAvailable(free, open: false));
    }

    [Fact]
    public async Task A_busy_https_port_leaves_http_up_and_says_why()
    {
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        try
        {
            var port = ((IPEndPoint)busy.LocalEndpoint).Port;
            await using var agent = await TestAgent.Start(s => s.Rete.Https = new HttpsSettings { Attivo = true, Porta = port });
            Assert.DoesNotContain(agent.App.Urls, u => u.StartsWith("https:", StringComparison.Ordinal));
            var status = await agent.Status();
            Assert.Contains(status["avvisi"]!.AsArray(), a => ((string)a!).Contains($"porta {port} è occupata"));
        }
        finally
        {
            busy.Stop();
        }
    }

    [Fact]
    public void The_first_password_cannot_be_set_from_the_network()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        Assert.False(Accesso.AdminAccess.IsLocal(context));
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        Assert.True(Accesso.AdminAccess.IsLocal(context));
    }

    [Fact]
    public async Task Cash_points_are_added_changed_and_removed_while_running()
    {
        await using var agent = await TestAgent.Start();
        var second = Path.Combine(agent.Root, "cassa2", "Scontrino.txt");
        var added = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Cassa bar", FileCassa = second });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var id = (string)(await Json(added))["id"]!;
        Assert.Equal("cassa2", id);

        var watch = Stopwatch.StartNew();
        while (!Directory.Exists(Path.GetDirectoryName(second)) && watch.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(20);
        await Write(second, Water);
        while (File.Exists(second) && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        Assert.False(File.Exists(second));   // la cassa nuova è sorvegliata subito

        var duplicate = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Doppia", FileCassa = agent.CashFile() });
        Assert.Contains("già di un'altra cassa", (string)(await Json(duplicate))["errore"]!);
        var relative = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Senza percorso", FileCassa = "Scontrino.txt" });
        Assert.Equal(HttpStatusCode.BadRequest, relative.StatusCode);

        var device = await agent.Pair("Schermo bar", id);
        var inUse = await agent.Admin.DeleteAsync($"/api/bo/casse/{id}");
        Assert.Contains("Schermo bar", (string)(await Json(inUse))["errore"]!);
        await agent.Admin.DeleteAsync($"/api/bo/dispositivi/{device.Id}");
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync($"/api/bo/casse/{id}")).StatusCode);
        Assert.Contains("almeno una cassa", (string)(await Json(await agent.Admin.DeleteAsync("/api/bo/casse/cassa1")))["errore"]!);

        var renamed = await agent.Admin.PutAsJsonAsync("/api/bo/casse/cassa1", new { Nome = "Cassa sala", FileCassa = agent.CashFile() });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Cassa sala", (string)(await agent.Status())["casse"]![0]!["nome"]!);
    }

    [Fact]
    public async Task The_cash_file_can_never_be_a_file_the_agent_would_wrongly_delete()
    {
        await using var agent = await TestAgent.Start();
        var settingsFile = Path.Combine(agent.DataFolder, SettingsStore.FileName);
        var notes = Path.Combine(agent.Root, "documenti", "appunti.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        await File.WriteAllTextAsync(notes, "Appunti del locale: da non cancellare");
        var monitor = Path.Combine(agent.Root, "cassa9", "Scontrino.txt");
        await Write(monitor, Coffee);

        foreach (var (file, error) in new[]
        {
            (settingsFile, "cartelle dell'Agent"),                           // le impostazioni: la revisione le ha fatte cancellare così
            (Path.Combine(agent.Root, "gestionale", "Monitor.xml"), ".txt"),   // un file di Passepartout, per sbaglio
            (Path.Combine(agent.DataFolder, "Scontrino.txt"), "cartelle dell'Agent"),
            (Path.Combine(agent.Root, "log", "copie", "x.txt"), "cartelle dell'Agent"),
            (notes, "non sembra quello del monitor"),
        })
        {
            var refused = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Sbagliata", FileCassa = file });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(error, (string)(await Json(refused))["errore"]!);
        }
        await Task.Delay(500);
        Assert.True(File.Exists(settingsFile));
        Assert.True(File.Exists(notes));

        // Un file del monitor già lì va bene; tolta la cassa, una cassa nuova non ne eredita i conteggi.
        var added = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Cassa 9", FileCassa = monitor });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var id = (string)(await Json(added))["id"]!;
        var watch = Stopwatch.StartNew();
        while (File.Exists(monitor) && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync($"/api/bo/casse/{id}")).StatusCode);
        var again = await agent.Admin.PostAsJsonAsync("/api/bo/casse", new { Nome = "Terrazza", FileCassa = Path.Combine(agent.Root, "terrazza", "Scontrino.txt") });
        Assert.NotEqual(id, (string)(await Json(again))["id"]!);
    }

    [Fact]
    public async Task The_statistics_period_is_counted_in_calendar_days()
    {
        await using var agent = await TestAgent.Start();
        await Write(agent.CashFile(), Coffee);
        var watch = Stopwatch.StartNew();
        while (File.Exists(agent.CashFile()) && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);
        await agent.App.StopAsync();   // salva statistiche.json
        var file = Path.Combine(agent.DataFolder, "statistiche.json");
        var data = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        var old = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd");
        data["Giorni"]![old] = new JsonObject { ["cassa1"] = new JsonObject { ["Vendite"] = 9 } };
        await File.WriteAllTextAsync(file, data.ToJsonString());

        await agent.Restart();
        await agent.Login();
        JsonArray Days(string json) => JsonNode.Parse(json)!["giorni"]!.AsArray();
        var today = Days(await agent.Admin.GetStringAsync("/api/bo/statistiche?giorni=1"));
        Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), (string)Assert.Single(today)!["giorno"]!);
        Assert.Single(Days(await agent.Admin.GetStringAsync("/api/bo/statistiche?giorni=7")));   // 10 giorni fa non è "7 giorni"
        Assert.Equal(2, Days(await agent.Admin.GetStringAsync("/api/bo/statistiche?giorni=31")).Count);
    }

    [Fact]
    public async Task A_test_print_reaches_the_printer()
    {
        var printer = new TcpListener(IPAddress.Loopback, 0);
        printer.Start();
        try
        {
            await using var agent = await TestAgent.Start();
            var created = await agent.Admin.PostAsJsonAsync("/api/bo/stampanti", new { Nome = "Banco", Collegamento = "rete", Indirizzo = $"127.0.0.1:{((IPEndPoint)printer.LocalEndpoint).Port}" });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var id = (string)(await Json(created))["id"]!;

            var printed = ReceiveAll(printer);
            var test = await agent.Admin.PostAsync($"/api/bo/stampanti/{id}/prova", null);
            var answer = await Json(test);
            Assert.Equal(HttpStatusCode.OK, test.StatusCode);
            Assert.Contains("STAMPA DI PROVA", (string)answer["testo"]!);
            Assert.Contains("[QR 49x49]", (string)answer["testo"]!);
            var bytes = await printed;
            Assert.Contains("STAMPA DI PROVA", Encoding.Latin1.GetString(bytes));
            Assert.Contains("Caffè", Encoding.Latin1.GetString(bytes));   // Windows-1252: è = E8

            var wrong = await agent.Admin.PostAsJsonAsync("/api/bo/stampanti", new { Nome = "Senza indirizzo", Collegamento = "rete", Indirizzo = "" });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        finally
        {
            printer.Stop();
        }
    }

    [Fact]
    public async Task Options_log_and_restart_from_the_back_office()
    {
        await using var agent = await TestAgent.Start();
        var settings = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/impostazioni"))!;
        Assert.Null(settings["Password"]);
        Assert.True((bool)settings["PasswordImpostata"]!);

        var probe = new TcpListener(IPAddress.Any, 0);
        probe.Start();
        var freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var options = new JsonObject
        {
            ["ChiusuraVenditaDopoSecondi"] = 90,
            ["ScontrinoDigitale"] = new JsonObject { ["MostraScontrino"] = true, ["CopiaCartacea"] = false, ["SecondiAVideo"] = 30, ["Dicitura"] = "Copia di cortesia" },
            ["Rete"] = new JsonObject { ["Porta"] = freePort, ["ApertoInRete"] = true, ["Https"] = new JsonObject { ["Attivo"] = false, ["Porta"] = 5443, ["Certificato"] = "", ["PasswordCertificato"] = null } },
        };
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsync("/api/bo/opzioni", JsonContent.Create(options))).StatusCode);

        var busy = new TcpListener(IPAddress.Any, 0);   // un altro programma sulla porta scelta
        busy.Start();
        try
        {
            options["Rete"]!["Porta"] = ((IPEndPoint)busy.LocalEndpoint).Port;
            var refused = await agent.Admin.PutAsync("/api/bo/opzioni", JsonContent.Create(options));
            Assert.Contains("già usata", (string)(await Json(refused))["errore"]!);
        }
        finally
        {
            busy.Stop();
        }
        options["Rete"]!["Porta"] = freePort;
        var status = await agent.Status();
        Assert.True((bool)status["riavvioNecessario"]!);   // la porta cambia al riavvio
        settings = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/impostazioni"))!;
        Assert.Equal(90, (int)settings["ChiusuraVenditaDopoSecondi"]!);
        Assert.True((bool)settings["ScontrinoDigitale"]!["MostraScontrino"]!);

        options["Rete"]!["Porta"] = 0;
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.Admin.PutAsync("/api/bo/opzioni", JsonContent.Create(options))).StatusCode);

        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("Opzioni cambiate dal back office"));

        var stopped = new TaskCompletionSource();
        agent.App.Lifetime.ApplicationStopping.Register(() => stopped.TrySetResult());
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsync("/api/bo/riavvia", null)).StatusCode);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(AgentApp.RestartRequested);   // da console Program rilancia l'Agent
        AgentApp.RestartRequested = false;
    }

    [Fact]
    public async Task Https_with_the_certificate_made_by_the_agent()
    {
        await using var agent = await TestAgent.Start(s => s.Rete.Https = new HttpsSettings { Attivo = true, Porta = 0 });
        var https = new Uri(agent.App.Urls.First(u => u.StartsWith("https:", StringComparison.Ordinal)));
        var authorityFile = Path.Combine(agent.DataFolder, "https", Server.HttpsCertificate.AuthorityFile);
        Assert.True(File.Exists(authorityFile));
        using var authority = X509CertificateLoader.LoadCertificateFromFile(authorityFile);

        bool Trusted(X509Certificate2 certificate)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(authority);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate);
        }

        string? seenThumbprint = null;
        HttpClientHandler Device(CookieContainer? cookies = null) => new()
        {
            CookieContainer = cookies ?? new CookieContainer(),
            AllowAutoRedirect = false,
            // Come un dispositivo con l'autorità installata: il nome deve tornare e la catena arrivare all'autorità.
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                seenThumbprint = certificate!.Thumbprint;
                return (errors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) == 0 && Trusted(certificate);
            },
        };
        using (var secure = new HttpClient(Device()) { BaseAddress = https })
            Assert.Contains("Back office", await secure.GetStringAsync("/backoffice"));

        var status = await agent.Status();
        Assert.True((bool)status["https"]!["generato"]!);
        var download = await agent.Admin.GetAsync("/api/bo/https/autorita");
        Assert.Equal(await File.ReadAllBytesAsync(authorityFile), await download.Content.ReadAsByteArrayAsync());

        // L'autorità vale solo per la rete locale: con la sua chiave non si garantisce un sito vero.
        using (var withKey = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(agent.DataFolder, "https", "autorita.pfx"), null))
        {
            X509Certificate2 Issue(string name)
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName(name);
                request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
                return request.Create(withKey, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(30), RandomNumberGenerator.GetBytes(16));
            }
            using var real = Issue("www.example.com");
            using var local = Issue("cassa.local");
            Assert.False(Trusted(real));
            Assert.True(Trusted(local));
        }

        // Un dispositivo abbinato su HTTPS ha il suo cookie ("_s"): su HTTP non viaggia, e abbinandolo anche lì non litiga.
        var cookies = new CookieContainer();
        using (var deviceHttps = new HttpClient(Device(cookies)) { BaseAddress = https })
        {
            var code = (string)(await Json(await deviceHttps.PostAsync("/api/abbinamento", null)))["codice"]!;
            Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = code, nome = "Tablet HTTPS", cassa = "cassa1" })).StatusCode);
            var poll = await deviceHttps.GetAsync("/api/abbinamento");
            Assert.Contains(poll.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bluetech_dispositivo_s=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(HttpStatusCode.OK, (await deviceHttps.GetAsync("/cliente")).StatusCode);
        }
        using (var deviceHttp = new HttpClient(new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false }) { BaseAddress = agent.Base })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await deviceHttp.GetAsync("/cliente")).StatusCode);
            var code = (string)(await Json(await deviceHttp.PostAsync("/api/abbinamento", null)))["codice"]!;
            Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PostAsJsonAsync("/api/bo/dispositivi", new { codice = code, nome = "Tablet HTTP", cassa = "cassa1" })).StatusCode);
            Assert.Equal("autorizzato", (string)(await Json(await deviceHttp.GetAsync("/api/abbinamento")))["stato"]!);
            Assert.Equal(HttpStatusCode.OK, (await deviceHttp.GetAsync("/cliente")).StatusCode);
        }

        // Al riavvio lo stesso certificato: i dispositivi non devono rifidarsi.
        var first = seenThumbprint;
        await agent.Restart();
        https = new Uri(agent.App.Urls.First(u => u.StartsWith("https:", StringComparison.Ordinal)));
        using (var again = new HttpClient(Device()) { BaseAddress = https })
            Assert.Contains("Back office", await again.GetStringAsync("/backoffice"));
        Assert.Equal(first, seenThumbprint);
    }

    [Fact]
    public async Task A_wrong_certificate_leaves_http_up_and_says_why()
    {
        await using var agent = await TestAgent.Start(s => s.Rete.Https = new HttpsSettings { Attivo = true, Porta = 0, Certificato = "non-esiste.pfx" });
        Assert.DoesNotContain(agent.App.Urls, u => u.StartsWith("https:", StringComparison.Ordinal));
        var status = await agent.Status();
        Assert.Contains(status["avvisi"]!.AsArray(), a => ((string)a!).Contains("HTTPS non attivo"));
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
        await using var agent = await TestAgent.Start();
        var device = await agent.Pair("Player");
        using var evil = new ClientWebSocket();
        evil.Options.Cookies = device.Cookies;
        evil.Options.SetRequestHeader("Origin", "https://evil.example");
        await Assert.ThrowsAsync<WebSocketException>(() => evil.ConnectAsync(new Uri($"ws://{agent.Base.Authority}/ws"), CancellationToken.None));

        using var page = new ClientWebSocket();   // la pagina servita dall'Agent stesso
        page.Options.Cookies = device.Cookies;
        page.Options.SetRequestHeader("Origin", $"http://{agent.Base.Authority}");
        await page.ConnectAsync(new Uri($"ws://{agent.Base.Authority}/ws"), CancellationToken.None);
        Assert.Equal("layout", (string)(await Receive(page, _ => true))["t"]!);

        using var post = new HttpRequestMessage(HttpMethod.Post, "/api/abbinamento");
        post.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.NewBrowser().Http.SendAsync(post)).StatusCode);
    }

    [Fact]
    public async Task A_device_gets_the_layout_forced_from_the_back_office_without_reconnecting()
    {
        await using var agent = await TestAgent.Start();
        var device = await agent.Pair("iPad Pro");
        using var socket = await agent.Connect(device);
        Assert.Null((await Receive(socket, Type("layout")))["force"]);
        await Receive(socket, Type("sync"));

        var update = await agent.Admin.PutAsJsonAsync("/api/bo/dispositivi/" + device.Id, new { nome = "iPad Pro", cassa = "cassa1", stampante = "", disposizione = "larga" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("wide", (string)(await Receive(socket, Type("layout")))["force"]!);   // subito, sullo stesso collegamento
        await Write(agent.CashFile(), Coffee);
        Assert.Equal("Caffè", (string)(await Receive(socket, Type("sale.line")))["line"]!["desc"]!);   // lo schermo non è stato staccato

        var refused = await agent.Admin.PutAsJsonAsync("/api/bo/dispositivi/" + device.Id, new { nome = "iPad Pro", cassa = "cassa1", disposizione = "obliqua" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("la disposizione è automatica, larga, stretta, verticale o palmare", (string)(await Json(refused))["errore"]!);

        // Senza il campo (un back office aperto prima dell'aggiornamento) la disposizione resta quella.
        await agent.Admin.PutAsJsonAsync("/api/bo/dispositivi/" + device.Id, new { nome = "iPad", cassa = "cassa1" });
        var settings = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/impostazioni"))!;
        Assert.Equal("larga", (string)settings["Dispositivi"]![0]!["Disposizione"]!);

        using var again = await agent.Connect(device);
        var first = await Receive(again, _ => true);
        Assert.Equal(("layout", "wide"), ((string)first["t"]!, (string)first["force"]!));   // al collegamento, per primo
    }

    [Fact]
    public async Task The_back_office_previews_any_cash_point()
    {
        await using var agent = await TestAgent.Start();
        Assert.Contains("Diagnostica Agent", await agent.Admin.GetStringAsync("/diagnostica?cassa=cassa1"));
        using var preview = await agent.Connect(null, "?cassa=cassa1", agent.AdminCookies);
        Assert.Equal("cfg", (string)(await Receive(preview, _ => true))["t"]!);
        Assert.Equal("sync", (string)(await Receive(preview, _ => true))["t"]!);
        await Write(agent.CashFile(), Coffee);
        Assert.Equal("Caffè", (string)(await Receive(preview, Type("sale.line")))["line"]!["desc"]!);
    }

    [Fact]
    public async Task A_display_copied_while_running_is_served_to_devices_and_always_revalidated()
    {
        await using var agent = await TestAgent.Start();
        var folder = Path.Combine(agent.Root, "display");
        Directory.CreateDirectory(Path.Combine(folder, "assets"));
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "<!doctype html><title>display v2</title>");
        await File.WriteAllTextAsync(Path.Combine(folder, "assets", "index-abc123.js"), "console.log(2)");

        var device = await agent.Pair("Player");
        var page = await device.Http.GetAsync("/cliente");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("display v2", html);
        Assert.Contains("/pwa/pwa.js", html);   // anche il display del design si installa come app
        Assert.True(page.Headers.CacheControl!.NoCache);
        var script = await device.Http.GetAsync("/assets/index-abc123.js");
        Assert.Equal(TimeSpan.FromDays(365), script.Headers.CacheControl!.MaxAge);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.NewBrowser().Http.GetAsync("/index.html")).StatusCode);   // la pagina solo da /cliente
    }

    [Fact]
    public async Task Stopping_with_a_display_connected_is_quick_and_closes_cleanly()
    {
        await using var agent = await TestAgent.Start();
        var device = await agent.Pair("Player");
        using var socket = await agent.Connect(device);
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
        await agent.App.StopAsync();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"arresto in {watch.Elapsed}");
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await closed);
    }

    static async Task<string?> Problem(TestAgent agent) => (string?)(await agent.Status())["casse"]![0]!["problema"];

    [Fact]
    public async Task A_folder_that_cannot_be_watched_keeps_the_agent_up_and_is_retried()
    {
        var root = Path.Combine(Path.GetTempPath(), "bt-agent-" + Guid.NewGuid().ToString("N"));
        var blocker = Path.Combine(root, "cassa1");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(blocker, "un file al posto della cartella");
        try
        {
            await using var agent = await TestAgent.Start(root: root);
            var watch = Stopwatch.StartNew();
            string? problem;
            while ((problem = await Problem(agent)) == null && watch.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);
            Assert.Contains("Non riesco a sorvegliare", problem);
            Assert.Contains("Back office", await agent.Admin.GetStringAsync("/backoffice"));   // il servizio è su

            File.Delete(blocker);   // il percorso torna valido: al prossimo tentativo (10 s) parte e crea la cartella
            while (((problem = await Problem(agent)) ?? "").Contains("Non riesco a sorvegliare") && watch.Elapsed < TimeSpan.FromSeconds(25))
                await Task.Delay(200);
            Assert.DoesNotContain("Non riesco a sorvegliare", problem ?? "");
            Assert.True(Directory.Exists(blocker));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task A_missing_replay_folder_is_reported()
    {
        await using var agent = await TestAgent.Start(options: new() { ["Agent:Replay"] = Path.Combine(Path.GetTempPath(), "non-esiste-" + Guid.NewGuid().ToString("N")) });
        var watch = Stopwatch.StartNew();
        string? problem;
        while ((problem = await Problem(agent)) == null && watch.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50);
        Assert.Contains("non esiste", problem);
        Assert.Equal("replay", (string)(await agent.Status())["casse"]![0]!["modalita"]!);
    }

    static async Task<byte[]> ReceiveAll(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var memory = new MemoryStream();
        await client.GetStream().CopyToAsync(memory);
        return memory.ToArray();
    }
}
