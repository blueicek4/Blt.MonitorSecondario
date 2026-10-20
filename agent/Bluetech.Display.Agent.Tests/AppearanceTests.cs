using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Xunit;
using static Bluetech.Display.Agent.Tests.TestAgent;

namespace Bluetech.Display.Agent.Tests;

/// <summary>L'aspetto dello schermo del cliente: temi, contrasto, promozioni, immagini e video, il messaggio cfg.</summary>
public sealed class AppearanceTests
{
    static AgentSettings WithCash()
    {
        var settings = AgentSettings.Defaults();
        settings.Casse[0].FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "Scontrino.txt");
        return settings;
    }

    static string? ThanksPng(string url) => DisplayConfig.QrPng(url);

    [Fact]
    public void The_eight_palettes_of_the_design_are_readable()
    {
        Assert.Equal(8, Palettes.All.Count);
        foreach (var (key, palette) in Palettes.All)
            Assert.All(ThemeColors.Pairs(palette), pair => Assert.True(pair.Ok, $"{key}: {pair.Label} {pair.Ratio}"));
        // I numeri nei commenti di themes.ts: 15,8:1 il testo di bluetech-notte, 4,6:1 il bianco sul primario.
        Assert.Equal(15.8, ThemeColors.Contrast("#E6EAF2", "#0B1017")!.Value, 1);
        Assert.Equal(4.6, ThemeColors.Contrast("#FFFFFF", "#1F6FEB")!.Value, 1);
        Assert.Null(ThemeColors.Contrast("rgba(255,255,255,0.5)", "#000000"));   // solo colori opachi
    }

    [Fact]
    public void Locale_colors_recalculate_what_the_theme_derives_from_them()
    {
        var day = Palettes.Get("bluetech", "giorno");
        var colors = LocaleColors.Of(day) with { Primary = "#B3261E", Text = "#1B1B1B" };
        var palette = ThemeColors.WithLocaleColors(day, colors);
        Assert.Equal("#B3261E", palette.TotalFill);   // il blocco del totale era il primario
        Assert.Equal("rgba(27,27,27,0.1)", palette.Line);   // i filetti seguono il testo, con la stessa trasparenza
        Assert.Equal("rgba(179,38,30,0.14)", palette.Glow);

        var night = Palettes.Get("bluetech", "notte");
        var dark = ThemeColors.WithLocaleColors(night, LocaleColors.Of(night) with { Bg = "#202020" });
        Assert.Equal(ThemeColors.Composite(night.Surface, "#202020"), dark.SurfaceSolid);   // la card opaca sul fondo nuovo
    }

    [Fact]
    public void Unreadable_colors_are_refused_with_the_nearest_fix()
    {
        var settings = WithCash();
        settings.Aspetto.Modo = "giorno";
        var day = Palettes.Get("bluetech", "giorno");
        settings.Aspetto.Colori = new ThemeColorSettings
        {
            Fondo = "#FFFFFF", Superficie = "#FFFFFF", Testo = "#A0A4AA", Tenue = day.Muted, Primario = day.Primary,
            SulPrimario = day.OnPrimary, Accento = day.Accent, Secondario = day.Secondary,
        };
        var problem = settings.Validate();
        Assert.NotNull(problem);
        Assert.Contains("poco leggibili", problem);
        Assert.Matches("il testo sul fondo [0-9],[0-9]:1", problem);
        Assert.Contains("serve almeno 4,5:1", problem);
        var fix = System.Text.RegularExpressions.Regex.Match(problem!, "testo (#[0-9A-F]{6})").Groups[1].Value;
        settings.Aspetto.Colori.Testo = fix;
        Assert.Null(settings.Validate());   // la correzione proposta passa
        Assert.InRange(ThemeColors.Contrast(fix, "#FFFFFF")!.Value, 4.5, 5.2);   // ed è la più vicina, non il nero

        settings.Aspetto.Colori.SulPrimario = "#3A6FD0";   // blu su blu
        Assert.Contains("testo sopra il primario #", settings.Validate());

        settings.Aspetto.Modo = "auto";   // con l'automatico valgono i colori del tema, come nel design
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void The_rest_of_the_look_is_checked_in_words()
    {
        var settings = WithCash();
        settings.Aspetto.Tema = "rococò";
        Assert.Contains("il tema è bluetech", settings.Validate());
        settings.Aspetto.Tema = "fine";
        settings.Aspetto.FontTitoli = "Comic Sans";
        Assert.Contains("font dei titoli", settings.Validate());
        settings.Aspetto.FontTitoli = "Syne";
        settings.Aspetto.GiornoDalle = "19:00";
        settings.Aspetto.GiornoAlle = "07:00";
        Assert.Contains("il giorno va dalle", settings.Validate());
        settings.Aspetto.GiornoDalle = "07:30";
        settings.Aspetto.GiornoAlle = "20:00";
        settings.Aspetto.Fondo = "immagine";
        Assert.Contains("sfondo a immagine", settings.Validate());
        settings.Aspetto.Fondo = "luci";
        settings.Aspetto.QrGrazie = new ThanksQrSettings { Attivo = true, Indirizzo = "javascript:alert(1)" };
        Assert.Contains("https://", settings.Validate());
        settings.Aspetto.QrGrazie.Indirizzo = "https://g.page/r/esempio/review";
        settings.Aspetto.Promozioni.Add(new PromotionConfig { Id = "promo1", Nome = "Aperitivo", NelleSlide = true });
        Assert.Contains("serve un'immagine o un video", settings.Validate());
        settings.Aspetto.Promozioni[0] = new PromotionConfig { Id = "promo1", Nome = "Aperitivo", NelleSlide = false, NelConto = true, Titolo = "Spritz a 5 €", Dalle = "18:00" };
        Assert.Contains("gli orari sono come 18:00", settings.Validate());
        settings.Aspetto.Promozioni[0].Alle = "20:00";
        settings.Aspetto.Promozioni[0].Casse = ["cassa9"];
        Assert.Contains("la cassa cassa9 non c'è", settings.Validate());
        settings.Aspetto.Promozioni[0].Casse = ["cassa1"];
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void A_promotion_follows_its_cash_points_days_hours_and_dates()
    {
        var friday = new PromotionConfig { Attiva = true, Giorni = [5], Dalle = "22:00", Alle = "02:00", Casse = ["cassa2"] };
        var fridayNight = new DateTime(2026, 10, 23, 23, 30, 0);   // venerdì
        Assert.True(DisplayConfig.Active(friday, "cassa2", fridayNight));
        Assert.False(DisplayConfig.Active(friday, "cassa1", fridayNight));   // un'altra cassa
        Assert.True(DisplayConfig.Active(friday, "cassa2", fridayNight.AddHours(2)));   // sabato all'1:30 è ancora venerdì sera
        Assert.False(DisplayConfig.Active(friday, "cassa2", fridayNight.AddHours(3)));   // le 2:30: finita
        Assert.False(DisplayConfig.Active(friday, "cassa2", fridayNight.AddDays(1)));   // sabato sera no

        var october = new PromotionConfig { Attiva = true, DalGiorno = new DateOnly(2026, 10, 1), AlGiorno = new DateOnly(2026, 10, 31) };
        Assert.True(DisplayConfig.Active(october, "cassa1", new DateTime(2026, 10, 31, 23, 59, 0)));
        Assert.False(DisplayConfig.Active(october, "cassa1", new DateTime(2026, 11, 1, 0, 1, 0)));
        Assert.False(DisplayConfig.Active(new PromotionConfig { Attiva = false }, "cassa1", fridayNight));
    }

    [Fact]
    public void The_config_message_carries_the_design_tokens_and_the_venue()
    {
        var settings = WithCash();
        settings.Casse.Add(new CashPointConfig { Id = "cassa2", Nome = "Bar", FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "bar", "Scontrino.txt") });
        var defaults = DisplayConfig.Build(settings, "cassa1", DateTime.Now, ThanksPng);
        Assert.Equal("bluetech", (string)defaults["preset"]!);
        var theme = defaults["theme"]!;
        Assert.Equal("#1F6FEB", (string)theme["cPrimary"]!);   // con l'automatico i token partono dalla notte
        Assert.Equal("auto", (string)theme["mode"]!);
        Assert.Equal("mesh", (string)theme["bgStyle"]!);
        Assert.Equal("light", (string)theme["motion"]!);
        Assert.Equal("beside", (string)theme["qrLayout"]!);
        Assert.Equal(7, (double)defaults["dayHours"]!["start"]!);
        Assert.Null(defaults["thanks"]);
        Assert.False((bool)defaults["pricesOnlyOnBill"]!);
        Assert.Null(defaults["ticker"]);

        var a = settings.Aspetto;
        a.Tema = "trattoria";
        a.Modo = "giorno";
        a.FontTitoli = "Cormorant Garamond";
        a.Raggio = 8;
        a.Vetro = 20;
        a.Animazioni = "ridotte";
        a.QrPagamento = "schermo";
        a.PrezziSoloSulConto = true;
        a.NomeLocale = "La Vela";
        a.Scorrevole = "Aperitivo dalle 18";
        a.GiornoDalle = "06:30";
        a.Media.Add(new MediaItem { Id = new string('a', 20) + ".jpg", Nome = "spritz.jpg", Tipo = "image/jpeg" });
        a.Media.Add(new MediaItem { Id = new string('b', 20) + ".mp4", Nome = "forno.mp4", Tipo = "video/mp4" });
        a.Media.Add(new MediaItem { Id = new string('f', 20) + ".png", Nome = "logo-scuro.png", Tipo = "image/png" });
        a.LogoChiaro = a.Media[2].Id;
        a.Promozioni.Add(new PromotionConfig { Id = "promo1", Nome = "Spritz", Media = a.Media[0].Id, SecondiImmagine = 9, NelConto = true, Titolo = "Spritz a 5 €" });
        a.Promozioni.Add(new PromotionConfig { Id = "promo2", Nome = "Forno", Media = a.Media[1].Id });
        a.Promozioni.Add(new PromotionConfig { Id = "promo3", Nome = "Solo bar", Media = a.Media[0].Id, Casse = ["cassa2"] });
        a.QrGrazie = new ThanksQrSettings { Attivo = true, Indirizzo = "https://g.page/r/esempio/review", Titolo = "Com'è andata?", Secondi = 40 };
        Assert.Null(settings.Validate());

        var config = DisplayConfig.Build(settings, "cassa1", DateTime.Now, ThanksPng);
        theme = config["theme"]!;
        Assert.Equal("trattoria", (string)config["preset"]!);
        Assert.Equal("#9C3A1F", (string)theme["cPrimary"]!);   // trattoria di giorno
        Assert.Equal("'Cormorant Garamond', Georgia, serif", (string)theme["fontDisplay"]!);
        Assert.Equal(8, (int)theme["radius"]!);
        Assert.Equal(20, (int)theme["glass"]!);
        Assert.Equal("eco", (string)theme["motion"]!);
        Assert.Equal("fullscreen", (string)theme["qrLayout"]!);
        Assert.Equal("light", (string)theme["footerVariant"]!);
        Assert.True((bool)config["pricesOnlyOnBill"]!);
        Assert.Equal(6.5, (double)config["dayHours"]!["start"]!);
        Assert.Equal("La Vela", (string)config["venue"]!["name"]!);
        Assert.Null(config["venue"]!["logo"]);
        Assert.Equal("/media/" + a.Media[2].Id, (string)config["venue"]!["logoLight"]!);   // il logo per i fondi chiari
        Assert.Equal("Aperitivo dalle 18", (string)config["ticker"]!);

        var ads = config["ads"]!.AsArray();
        Assert.Equal(["promo1", "promo2"], ads.Select(s => (string)s!["id"]!));   // la promozione della cassa 2 no
        Assert.Equal(9000, (int)ads[0]!["durationMs"]!);
        Assert.Equal("/media/" + a.Media[0].Id, (string)ads[0]!["src"]!);
        Assert.Null(ads[1]!["durationMs"]);   // un video dura quanto dura
        Assert.Equal("video", (string)ads[1]!["media"]!);
        var card = Assert.Single(config["promos"]!.AsArray());
        Assert.Equal("Spritz a 5 €", (string)card!["title"]!);

        var thanks = config["thanks"]!;
        Assert.Equal(40, (int)thanks["seconds"]!);
        var png = Convert.FromBase64String(((string)thanks["png"]!)["data:image/png;base64,".Length..]);
        var image = QrImage.Decode(png)!;
        Assert.Equal(image.Width, image.Height);
        Assert.Equal(0, (image.Width - 21) % 4);   // 21, 25, 29…: un pixel per modulo, senza quiet zone
        Assert.True(image.Dark[0] && image.Dark[6] && !image.Dark[7]);   // il finder pattern in alto a sinistra
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1 }, "image/jpeg")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0 }, "image/gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 }, "image/webp")]
    [InlineData(new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D, 0, 0, 2, 0 }, "video/mp4")]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 1, 0x42, 0xF7, 0x81, 1, 0x42, 0xF2, 0x81 }, "video/webm")]
    [InlineData(new byte[] { 0, 0, 0, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74, 0x20, 0x20, 0, 0, 2, 0 }, null)]   // .mov di QuickTime
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x20, 0x78, 0x6D, 0x6C, 0x6E, 0x73, 0x3D, 0x22, 0x68, 0x74, 0x74, 0x70 }, null)]   // <svg xmlns="http
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF, 0, 0 }, null)]   // un .exe
    public void Media_types_come_from_the_content_not_the_name(byte[] head, string? type)
    {
        Assert.Equal(type, MediaLibrary.Detect(head)?.Type);
    }

    /// <summary>Un PNG vero e piccolo: un QR.</summary>
    static byte[] SamplePng() => Convert.FromBase64String(DisplayConfig.QrPng("https://example.com/logo")["data:image/png;base64,".Length..]);

    static async Task<JsonObject> Upload(HttpClient admin, byte[] bytes, string name, string type = "image/png")
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return await Json(await admin.PostAsync("/api/bo/media?nome=" + Uri.EscapeDataString(name), content));
    }

    [Fact]
    public async Task Images_and_videos_are_uploaded_shown_only_to_displays_and_removed_when_unused()
    {
        await using var agent = await Start();
        var uploaded = await Upload(agent.Admin, SamplePng(), "logo.png");
        var media = uploaded["media"]!;
        var id = (string)media["Id"]!;
        Assert.Matches("^[0-9a-f]{20}\\.png$", id);
        Assert.Equal("logo.png", (string)media["Nome"]!);
        Assert.True(File.Exists(Path.Combine(agent.DataFolder, "media", id)));
        Assert.Equal(id, (string)(await Upload(agent.Admin, SamplePng(), "di-nuovo.png"))["media"]!["Id"]!);   // lo stesso file è uno solo

        var refused = await Upload(agent.Admin, Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "pagina.png");
        Assert.Contains("non è un'immagine", (string)refused["errore"]!);
        Assert.Single(Directory.GetFiles(Path.Combine(agent.DataFolder, "media")));   // niente avanzi del caricamento

        var device = await agent.Pair("Tablet");
        var served = await device.Http.GetAsync("/media/" + id);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", served.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await agent.NewBrowser().Http.GetAsync("/media/" + id)).StatusCode);   // chi non è autorizzato non vede niente
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.GetAsync("/media/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.Http.GetAsync("/media/..%2Fimpostazioni.json")).StatusCode);

        var look = await Json(await agent.Admin.GetAsync("/api/bo/aspetto"));
        var aspetto = look["Aspetto"]!.AsObject();
        aspetto["Logo"] = id;
        aspetto["NomeLocale"] = "La Vela";
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/aspetto", aspetto)).StatusCode);
        var inUse = await Json(await agent.Admin.DeleteAsync("/api/bo/media/" + id));
        Assert.Contains("lo usa il logo", (string)inUse["errore"]!);

        aspetto["Logo"] = null;
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/aspetto", aspetto)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/media/" + id)).StatusCode);
        Assert.False(File.Exists(Path.Combine(agent.DataFolder, "media", id)));
        Assert.Equal(HttpStatusCode.NotFound, (await device.Http.GetAsync("/media/" + id)).StatusCode);
    }

    [Fact]
    public async Task A_new_look_reaches_the_displays_at_once()
    {
        await using var agent = await Start();
        var device = await agent.Pair("Player cassa");
        using var socket = await agent.Connect(device);
        Assert.Equal("bluetech", (string)(await Receive(socket, Type("cfg")))["preset"]!);
        await Receive(socket, Type("sync"));

        var uploaded = (string)(await Upload(agent.Admin, SamplePng(), "aperitivo.png"))["media"]!["Id"]!;
        var aspetto = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!.AsObject();
        aspetto["Tema"] = "cocktail";
        aspetto["NomeLocale"] = "Bar Centrale";
        aspetto["Scorrevole"] = "Happy hour dalle 18 alle 20";
        aspetto["Promozioni"] = new JsonArray(new JsonObject { ["Nome"] = "Happy hour", ["Media"] = uploaded, ["NelleSlide"] = true });
        var saved = await agent.Admin.PutAsJsonAsync("/api/bo/aspetto", aspetto);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var config = await Receive(socket, Type("cfg"));
        Assert.Equal("cocktail", (string)config["preset"]!);
        Assert.Equal("Bar Centrale", (string)config["venue"]!["name"]!);
        Assert.Equal("Happy hour dalle 18 alle 20", (string)config["ticker"]!);
        var slide = Assert.Single(config["ads"]!.AsArray())!;
        Assert.Equal("promo1", (string)slide["id"]!);   // l'id lo dà l'Agent
        Assert.Equal("/media/" + uploaded, (string)slide["src"]!);
        Assert.Equal(HttpStatusCode.OK, (await device.Http.GetAsync((string)slide["src"]!)).StatusCode);

        var stored = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(agent.DataFolder, SettingsStore.FileName)))!;
        Assert.Equal("cocktail", (string)stored["Aspetto"]!["Tema"]!);
        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("Aspetto cambiato dal back office"));
    }

    [Fact]
    public async Task A_look_that_cannot_be_read_is_refused_and_the_preview_says_why()
    {
        await using var agent = await Start();
        var aspetto = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!.AsObject();
        aspetto["Modo"] = "notte";
        aspetto["Colori"] = new JsonObject
        {
            ["Fondo"] = "#0B1017", ["Superficie"] = "rgba(255,255,255,0.055)", ["Testo"] = "#30343A", ["Tenue"] = "#9AA6BA",
            ["Primario"] = "#1F6FEB", ["SulPrimario"] = "#FFFFFF", ["Accento"] = "#3DDC97", ["Secondario"] = "#8FA3C0",
        };
        var refused = await agent.Admin.PutAsJsonAsync("/api/bo/aspetto", aspetto);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("La correzione più vicina: testo #", (string)(await Json(refused))["errore"]!);

        var preview = await Json(await agent.Admin.PostAsJsonAsync("/api/bo/aspetto/anteprima?cassa=cassa1", aspetto));
        Assert.Equal("cfg", (string)preview["cfg"]!["t"]!);
        Assert.Equal("#30343A", (string)preview["cfg"]!["theme"]!["cText"]!);   // la bozza com'è: il display ripiegherebbe
        Assert.Contains(preview["problemi"]!.AsArray(), p => ((string)p!).Contains("poco leggibili"));
        Assert.NotNull(preview["anteprimaQr"]);
        var stored = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!;
        Assert.Equal("auto", (string)stored["Modo"]!);   // l'anteprima non salva
    }

    [Fact]
    public async Task The_customer_page_brings_its_theme_fonts_and_the_preview_for_the_back_office()
    {
        await using var agent = await Start();
        var stranger = agent.NewBrowser();
        var script = await stranger.Http.GetStringAsync("/aspetto/tema.js");
        Assert.Contains("\"bluetech-notte\":{\"bg\":\"#0B1017\"", script);
        Assert.Contains("\"Space Grotesk\"", script);
        var font = await stranger.Http.GetAsync("/fonts/archivo-latin-wght-normal.woff2");
        Assert.Equal(HttpStatusCode.OK, font.StatusCode);
        Assert.Equal("font/woff2", font.Content.Headers.ContentType!.MediaType);
        Assert.Contains("font-family: 'Syne'", await stranger.Http.GetStringAsync("/fonts/fonts.css"));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync("/fonts/impostazioni.json")).StatusCode);

        var page = await agent.Admin.GetStringAsync("/cliente?anteprima=demo");
        Assert.Contains("/aspetto/tema.js", page);
        Assert.DoesNotContain("manifest.webmanifest", page);   // l'anteprima non è un'app da installare
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Http.GetAsync("/cliente?anteprima=demo")).StatusCode);
        var device = await agent.Pair("Tablet");
        Assert.Equal("/cliente", (await device.Http.GetAsync("/cliente?anteprima=demo")).Headers.Location!.OriginalString);
        Assert.Contains("manifest.webmanifest", await device.Http.GetStringAsync("/cliente"));
    }

    [Fact]
    public async Task Removing_a_cash_point_switches_off_the_promotions_that_were_only_for_it()
    {
        await using var agent = await Start(s =>
        {
            AddCash(s, "cassa2", "Bar");
            s.Aspetto.Promozioni.Add(new PromotionConfig { Id = "promo1", Nome = "Solo bar", NelleSlide = false, NelConto = true, Titolo = "Caffè e cornetto", Casse = ["cassa2"] });
            s.Aspetto.Promozioni.Add(new PromotionConfig { Id = "promo2", Nome = "Tutte e due", NelleSlide = false, NelConto = true, Titolo = "Dolce del giorno", Casse = ["cassa1", "cassa2"] });
        });
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/casse/cassa2")).StatusCode);
        var promos = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!["Promozioni"]!.AsArray();
        Assert.False((bool)promos[0]!["Attiva"]!);   // non passa a tutte le casse
        Assert.Empty(promos[0]!["Casse"]!.AsArray());
        Assert.True((bool)promos[1]!["Attiva"]!);
        Assert.Equal(["cassa1"], promos[1]!["Casse"]!.AsArray().Select(c => (string)c!));
    }

    [Fact]
    public void Missing_files_are_dropped_from_the_list_and_stray_ones_from_the_disk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bt-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder);
            var kept = new string('c', 20) + ".png";
            var gone = new string('d', 20) + ".jpg";
            store.Update(s =>
            {
                s.Casse[0].FileCassa = Path.Combine(folder, "cassa1", "Scontrino.txt");
                s.Aspetto.Media.Add(new MediaItem { Id = kept, Nome = "logo.png", Tipo = "image/png" });
                s.Aspetto.Media.Add(new MediaItem { Id = gone, Nome = "spritz.jpg", Tipo = "image/jpeg" });
                s.Aspetto.Logo = kept;
                s.Aspetto.Promozioni.Add(new PromotionConfig { Id = "promo1", Nome = "Spritz", Media = gone });
                return null;
            });
            var media = Path.Combine(folder, "media");
            Directory.CreateDirectory(media);
            File.WriteAllBytes(Path.Combine(media, kept), [1]);
            File.WriteAllBytes(Path.Combine(media, new string('e', 20) + ".png"), [1]);   // non è nell'elenco
            File.WriteAllBytes(Path.Combine(media, ".caricamento-123.tmp"), [1]);
            File.WriteAllBytes(Path.Combine(media, "leggimi.txt"), [1]);   // non è dell'Agent: resta
            var runtime = new Server.AgentRuntime { DataFolder = folder, LogFolder = folder, DisplayFolder = folder, StartupNetwork = new NetworkSettings() };
            var notes = new MediaLibrary(store, runtime, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaLibrary>.Instance).Tidy();

            Assert.Contains(notes, n => n.Contains("spritz.jpg"));
            Assert.Contains(notes, n => n.Contains("la promozione Spritz è tolta"));
            Assert.Equal([kept], store.Current.Aspetto.Media.Select(m => m.Id));
            Assert.Equal(kept, store.Current.Aspetto.Logo);
            Assert.Empty(store.Current.Aspetto.Promozioni);
            Assert.Equal([kept, "leggimi.txt"], Directory.GetFiles(media).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
