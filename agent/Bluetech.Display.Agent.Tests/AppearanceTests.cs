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

/// <summary>L'aspetto dello schermo del cliente: temi, contrasto, gallerie e set di reclame, immagini e video, il messaggio cfg.</summary>
public sealed class AppearanceTests
{
    static AgentSettings WithCash()
    {
        var settings = AgentSettings.Defaults();
        settings.Casse[0].FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "Scontrino.txt");
        return settings;
    }

    static string? Qr(string url) => DisplayConfig.QrPng(url);

    static MediaItem Photo(char c, string name) => new() { Id = new string(c, 20) + ".jpg", Nome = name, Tipo = "image/jpeg" };
    static MediaItem Video(char c, string name) => new() { Id = new string(c, 20) + ".mp4", Nome = name, Tipo = "video/mp4" };

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
        settings.Aspetto.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Registrati" });
        Assert.Contains("La promozione Registrati: scrivi un titolo o un testo, o scegli un'immagine o un QR", settings.Validate());
        settings.Aspetto.Promozioni[0].Qr = "javascript:alert(1)";
        Assert.Contains("l'indirizzo del QR è come https://", settings.Validate());
        settings.Aspetto.Promozioni[0].Qr = "https://example.com/registrati";
        settings.Aspetto.Set.Add(new AdSetConfig { Id = "set1", Nome = "Aperitivo", Elementi = [new AdItemConfig { Id = "promo1" }] });
        Assert.Contains("Il set Aperitivo: scegli dove va", settings.Validate());
        settings.Aspetto.Set[0].Dove = ["attesa"];
        settings.Aspetto.Set[0].Dalle = "18:00";
        Assert.Contains("gli orari sono come 18:00", settings.Validate());
        settings.Aspetto.Set[0].Alle = "20:00";
        settings.Aspetto.Set[0].Casse = ["cassa9"];
        Assert.Contains("la cassa cassa9 non c'è", settings.Validate());
        settings.Aspetto.Set[0].Casse = ["cassa1"];
        settings.Aspetto.Set[0].Elementi.Add(new AdItemConfig { Id = "promo9" });
        Assert.Contains("un elemento non c'è più (promo9)", settings.Validate());
        settings.Aspetto.Set[0].Elementi.RemoveAt(1);
        settings.Aspetto.Set[0].Ordine = "a caso";
        Assert.Contains("l'ordine è elenco, casuale o alfabetico", settings.Validate());
        settings.Aspetto.Set[0].Ordine = "casuale";
        settings.Aspetto.Set[0].QrDopo = 2;
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void A_set_follows_its_cash_points_days_hours_and_dates()
    {
        var friday = new AdSetConfig { Giorni = [5], Dalle = "22:00", Alle = "02:00", Casse = ["cassa2"] };
        var fridayNight = new DateTime(2026, 10, 23, 23, 30, 0);   // venerdì
        Assert.True(DisplayConfig.Active(friday, "cassa2", fridayNight));
        Assert.False(DisplayConfig.Active(friday, "cassa1", fridayNight));   // un'altra cassa
        Assert.True(DisplayConfig.Active(friday, "cassa2", fridayNight.AddHours(2)));   // sabato all'1:30 è ancora venerdì sera
        Assert.Equal(new DateOnly(2026, 10, 23), DisplayConfig.ServiceDay(friday, "cassa2", fridayNight.AddHours(2)));
        Assert.False(DisplayConfig.Active(friday, "cassa2", fridayNight.AddHours(3)));   // le 2:30: finita
        Assert.False(DisplayConfig.Active(friday, "cassa2", fridayNight.AddDays(1)));   // sabato sera no

        var october = new AdSetConfig { DalGiorno = new DateOnly(2026, 10, 1), AlGiorno = new DateOnly(2026, 10, 31) };
        Assert.True(DisplayConfig.Active(october, "cassa1", new DateTime(2026, 10, 31, 23, 59, 0)));
        Assert.False(DisplayConfig.Active(october, "cassa1", new DateTime(2026, 11, 1, 0, 1, 0)));
        Assert.False(DisplayConfig.Active(new AdSetConfig { Attivo = false }, "cassa1", fridayNight));
    }

    [Fact]
    public void For_each_place_the_first_set_that_holds_wins()
    {
        var settings = WithCash();
        var a = settings.Aspetto;
        a.Media.AddRange([Photo('a', "spritz.jpg"), Photo('b', "tagliere.jpg"), Photo('c', "pizza.jpg")]);
        a.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Ottobre", Titolo = "Castagne e vin brulé", AlGiorno = new DateOnly(2026, 10, 31) });
        a.Set.Add(new AdSetConfig { Id = "set1", Nome = "Aperitivo", Dove = ["attesa"], Dalle = "18:00", Alle = "20:00", Elementi = [new() { Id = a.Media[0].Id }, new() { Id = a.Media[1].Id }] });
        a.Set.Add(new AdSetConfig { Id = "set2", Nome = "Solo ottobre", Dove = ["conto"], Elementi = [new() { Id = "promo1" }] });
        a.Set.Add(new AdSetConfig { Id = "set3", Nome = "Sempre", Dove = ["attesa", "conto"], Elementi = [new() { Id = a.Media[2].Id }] });
        a.Set.Add(new AdSetConfig { Id = "set4", Nome = "Spento", Attivo = false, Dove = ["grazie"], Elementi = [new() { Id = "promo1" }] });
        Assert.Null(settings.Validate());

        var aperitivo = new DateTime(2026, 10, 20, 19, 0, 0);
        var onAir = DisplayConfig.OnAir(settings, "cassa1", aperitivo);
        Assert.Equal("set1", onAir["attesa"]!.Id);
        Assert.Equal("set2", onAir["conto"]!.Id);
        Assert.Null(onAir["grazie"]);   // un set spento non va in onda
        Assert.Equal(["in onda", "in onda", "coperto", "spento"], DisplayConfig.States(settings, aperitivo).Values);   // Sempre vale, ma vincono i due sopra

        var evening = new DateTime(2026, 10, 20, 21, 0, 0);
        Assert.Equal("set3", DisplayConfig.OnAir(settings, "cassa1", evening)["attesa"]!.Id);
        Assert.Equal("fuori orario", DisplayConfig.States(settings, evening)["set1"]);

        // A novembre la promozione di ottobre è scaduta: il set non ha niente da mostrare e va in onda quello dopo.
        var november = new DateTime(2026, 11, 2, 12, 0, 0);
        Assert.Equal("set3", DisplayConfig.OnAir(settings, "cassa1", november)["conto"]!.Id);
        Assert.Equal("vuoto", DisplayConfig.States(settings, november)["set2"]);
        a.Set.Insert(0, new AdSetConfig { Id = "set5", Nome = "Anche il conto", Dove = ["conto"], Elementi = [new() { Id = a.Media[0].Id }] });
        Assert.Equal("set5", DisplayConfig.OnAir(settings, "cassa1", november)["conto"]!.Id);
        Assert.Equal("in onda", DisplayConfig.States(settings, november)["set3"]);   // nell'attesa sì

        // L'anteprima di un set lo mostra nei suoi posti anche spento e fuori orario.
        Assert.Equal("set4", DisplayConfig.OnAir(settings, "cassa1", november, forceSet: "set4")["grazie"]!.Id);
    }

    static List<DisplayConfig.Entry> Entries(params (string Id, bool Qr)[] items) =>
        items.Select(i => new DisplayConfig.Entry(i.Id, i.Id, i.Qr ? null : Photo('a', i.Id), i.Qr ? new PromoConfig { Id = i.Id, Qr = "https://example.com/" + i.Id } : null, 8)).ToList();

    [Fact]
    public void In_order_by_name_or_at_random_with_a_qr_promotion_after_every_few()
    {
        var items = Entries(("cantina", false), ("aperitivo", false), ("brunch", false), ("registrati", true), ("recensioni", true));
        var inOrder = DisplayConfig.Expand(new AdSetConfig { Id = "set1", Ordine = "elenco" }, items);
        Assert.Equal(["cantina", "aperitivo", "brunch", "registrati", "recensioni"], inOrder.Select(i => i.Id));   // il display la ripete
        var byName = DisplayConfig.Expand(new AdSetConfig { Id = "set1", Ordine = "alfabetico" }, items);
        Assert.Equal(["aperitivo", "brunch", "cantina", "recensioni", "registrati"], byName.Select(i => i.Id));

        // In ordine con un QR dopo ogni foto: le due liste fanno giri interi (3 foto e 2 QR: 6 blocchi).
        var everyOther = DisplayConfig.Expand(new AdSetConfig { Id = "set1", Ordine = "elenco", QrDopo = 1 }, items);
        Assert.Equal(["cantina", "registrati", "aperitivo", "recensioni", "brunch", "registrati", "cantina", "recensioni", "aperitivo", "registrati", "brunch", "recensioni"],
            everyOther.Select(i => i.Id));

        // A caso, due foto e un QR: "due slide a caso, un QR, due slide, un altro QR".
        var set = new AdSetConfig { Id = "set7", Ordine = "casuale", QrDopo = 2 };
        var random = DisplayConfig.Expand(set, items);
        Assert.InRange(random.Count, 60, 300);
        Assert.Equal(0, random.Count % 3);
        for (var i = 0; i < random.Count; i++)
            Assert.Equal(i % 3 == 2, random[i].HasQr);
        Assert.Equal(3, random.Where(i => !i.HasQr).Select(i => i.Id).Distinct().Count());
        Assert.Single(random.Where(i => !i.HasQr).GroupBy(i => i.Id).Select(g => g.Count()).Distinct());   // ognuna lo stesso numero di volte
        Assert.Single(random.Where(i => i.HasQr).GroupBy(i => i.Id).Select(g => g.Count()).Distinct());
        Assert.Equal(random.Select(i => i.Id), DisplayConfig.Expand(set, items).Select(i => i.Id));   // sempre la stessa: il cfg non cambia a ogni controllo
        Assert.NotEqual(random.Select(i => i.Id), DisplayConfig.Expand(new AdSetConfig { Id = "set8", Ordine = "casuale", QrDopo = 2 }, items).Select(i => i.Id));

        // A caso senza la regola: giri mescolati, mai lo stesso elemento due volte di fila.
        var shuffled = DisplayConfig.Expand(new AdSetConfig { Id = "set2", Ordine = "casuale" }, items);
        Assert.Equal(60, shuffled.Count);
        Assert.All(Enumerable.Range(1, shuffled.Count - 1), i => Assert.NotEqual(shuffled[i - 1].Id, shuffled[i].Id));
        Assert.All(shuffled.Chunk(5), round => Assert.Equal(5, round.Select(i => i.Id).Distinct().Count()));

        // Solo promozioni con il QR, o nessuna: la regola non c'entra.
        Assert.Equal(["cantina", "aperitivo"], DisplayConfig.Expand(new AdSetConfig { Ordine = "elenco", QrDopo = 2 }, items.Take(2).ToList()).Select(i => i.Id));
        Assert.True(DisplayConfig.Expand(new AdSetConfig { Ordine = "casuale", QrDopo = 3 }, items.Skip(3).ToList()).All(i => i.HasQr));
    }

    [Fact]
    public void The_config_message_carries_the_design_tokens_and_the_venue()
    {
        var settings = WithCash();
        settings.Casse.Add(new CashPointConfig { Id = "cassa2", Nome = "Bar", FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "bar", "Scontrino.txt") });
        var defaults = DisplayConfig.Build(settings, "cassa1", DateTime.Now, Qr);
        Assert.Equal("bluetech", (string)defaults["preset"]!);
        var theme = defaults["theme"]!;
        Assert.Equal("#1F6FEB", (string)theme["cPrimary"]!);   // con l'automatico i token partono dalla notte
        Assert.Equal("auto", (string)theme["mode"]!);
        Assert.Equal("mesh", (string)theme["bgStyle"]!);
        Assert.Equal("light", (string)theme["motion"]!);
        Assert.Equal("beside", (string)theme["qrLayout"]!);
        Assert.Equal(7, (double)defaults["dayHours"]!["start"]!);
        Assert.Empty(defaults["playlists"]!["idle"]!.AsArray());
        Assert.Empty(defaults["promoCards"]!.AsObject());
        Assert.Equal(30, (int)defaults["thanksSeconds"]!);
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
        a.Media.Add(Photo('a', "spritz.jpg"));
        a.Media.Add(Video('b', "forno.mp4"));
        a.Media.Add(new MediaItem { Id = new string('f', 20) + ".png", Nome = "logo-scuro.png", Tipo = "image/png" });
        a.LogoChiaro = a.Media[2].Id;
        a.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Spritz", Immagine = a.Media[0].Id, Titolo = "Spritz a 5 €" });
        a.Promozioni.Add(new PromoConfig { Id = "promo2", Nome = "Registrati", Titolo = "Diventa cliente", Qr = "https://example.com/registrati", QrDidascalia = "Inquadra e registrati" });
        a.Set.Add(new AdSetConfig { Id = "set1", Nome = "Solo bar", Dove = ["attesa"], Casse = ["cassa2"], Elementi = [new() { Id = a.Media[1].Id }] });
        a.Set.Add(new AdSetConfig { Id = "set2", Nome = "Sempre", Dove = ["attesa", "conto"], Secondi = 9, Elementi = [new() { Id = a.Media[0].Id }, new() { Id = a.Media[1].Id }, new() { Id = "promo1", Secondi = 12 }] });
        a.Set.Add(new AdSetConfig { Id = "set3", Nome = "Grazie", Dove = ["grazie"], Elementi = [new() { Id = "promo2" }] });
        a.SecondiGrazie = 40;
        Assert.Null(settings.Validate());

        var config = DisplayConfig.Build(settings, "cassa1", DateTime.Now, Qr);
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
        Assert.Equal(40, (int)config["thanksSeconds"]!);

        var idle = config["playlists"]!["idle"]!.AsArray();
        Assert.Equal([a.Media[0].Id, a.Media[1].Id, "promo1"], idle.Select(i => (string)i!["id"]!));   // il set della cassa 2 no
        Assert.Equal(["image", "video", "promo"], idle.Select(i => (string)i!["kind"]!));
        Assert.Equal(9000, (int)idle[0]!["durationMs"]!);   // i secondi del set
        Assert.Equal("/media/" + a.Media[0].Id, (string)idle[0]!["src"]!);
        Assert.Null(idle[1]!["durationMs"]);   // un video dura quanto dura
        Assert.Equal(12000, (int)idle[2]!["durationMs"]!);   // i secondi dell'elemento
        Assert.Equal(idle.ToJsonString(), config["playlists"]!["sale"]!.ToJsonString());   // lo stesso set accanto al conto
        Assert.Equal(["promo2"], config["playlists"]!["thanks"]!.AsArray().Select(i => (string)i!["id"]!));
        Assert.Equal([a.Media[0].Id, a.Media[1].Id], config["ads"]!.AsArray().Select(s => (string)s!["id"]!));   // le AdSlide del design: niente promozioni

        var cards = config["promoCards"]!.AsObject();
        Assert.Equal(["promo1", "promo2"], cards.Select(c => c.Key));
        Assert.Equal("Spritz a 5 €", (string)cards["promo1"]!["title"]!);
        Assert.Equal("/media/" + a.Media[0].Id, (string)cards["promo1"]!["image"]!);
        Assert.Null(cards["promo1"]!["qr"]);
        Assert.Equal("Inquadra e registrati", (string)cards["promo2"]!["qr"]!["caption"]!);
        var png = Convert.FromBase64String(((string)cards["promo2"]!["qr"]!["png"]!)["data:image/png;base64,".Length..]);
        var image = QrImage.Decode(png)!;
        Assert.Equal(image.Width, image.Height);
        Assert.Equal(0, (image.Width - 21) % 4);   // 21, 25, 29…: un pixel per modulo, senza quiet zone
        Assert.True(image.Dark[0] && image.Dark[6] && !image.Dark[7]);   // il finder pattern in alto a sinistra

        var bar = DisplayConfig.Build(settings, "cassa2", DateTime.Now, Qr);
        Assert.Equal([a.Media[1].Id], bar["playlists"]!["idle"]!.AsArray().Select(i => (string)i!["id"]!));   // alla cassa 2 vince il set più in alto

        // L'anteprima di una promozione la mette da sola in tutti e tre i posti.
        var preview = DisplayConfig.Build(settings, "cassa1", DateTime.Now, Qr, onlyPromo: "promo2");
        Assert.All(new[] { "idle", "sale", "thanks" }, key => Assert.Equal(["promo2"], preview["playlists"]![key]!.AsArray().Select(i => (string)i!["id"]!)));
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
        aspetto["Promozioni"] = new JsonArray(new JsonObject { ["Nome"] = "Registrati", ["Titolo"] = "Diventa cliente", ["Qr"] = " https://example.com/registrati " });
        aspetto["Set"] = new JsonArray(new JsonObject
        {
            ["Nome"] = "Happy hour", ["Dove"] = new JsonArray("attesa"),
            ["Elementi"] = new JsonArray(new JsonObject { ["Id"] = uploaded }, new JsonObject { ["Id"] = "promo1" }),
        });
        var saved = await agent.Admin.PutAsJsonAsync("/api/bo/aspetto", aspetto);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var config = await Receive(socket, Type("cfg"));
        Assert.Equal("cocktail", (string)config["preset"]!);
        Assert.Equal("Bar Centrale", (string)config["venue"]!["name"]!);
        Assert.Equal("Happy hour dalle 18 alle 20", (string)config["ticker"]!);
        var idle = config["playlists"]!["idle"]!.AsArray();
        Assert.Equal([uploaded, "promo1"], idle.Select(i => (string)i!["id"]!));   // l'id della promozione lo dà l'Agent
        Assert.Equal("/media/" + uploaded, (string)idle[0]!["src"]!);
        Assert.Equal(HttpStatusCode.OK, (await device.Http.GetAsync((string)idle[0]!["src"]!)).StatusCode);
        Assert.StartsWith("data:image/png;base64,", (string)config["promoCards"]!["promo1"]!["qr"]!["png"]!);

        var look = await Json(await agent.Admin.GetAsync("/api/bo/aspetto"));
        Assert.Equal("https://example.com/registrati", (string)look["Aspetto"]!["Promozioni"]![0]!["Qr"]!);
        Assert.Equal("set1", (string)look["InOnda"]!["cassa1"]!["attesa"]!);
        Assert.Null(look["InOnda"]!["cassa1"]!["conto"]);
        Assert.Equal("in onda", (string)look["StatoSet"]!["set1"]!);

        var stored = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(agent.DataFolder, SettingsStore.FileName)))!;
        Assert.Equal("cocktail", (string)stored["Aspetto"]!["Tema"]!);
        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("Aspetto cambiato dal back office"));
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("attesa: Happy hour, accanto al conto: niente, Grazie: niente"));
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

        aspetto["Promozioni"] = new JsonArray(new JsonObject { ["Id"] = "promo1", ["Nome"] = "Venerdì", ["Titolo"] = "Musica dal vivo" });
        aspetto["Set"] = new JsonArray(new JsonObject
        {
            ["Id"] = "set1", ["Nome"] = "Venerdì sera", ["Attivo"] = false, ["Dove"] = new JsonArray("attesa", "conto"),
            ["Elementi"] = new JsonArray(new JsonObject { ["Id"] = "promo1" }),
        });
        var preview = await Json(await agent.Admin.PostAsJsonAsync("/api/bo/aspetto/anteprima?cassa=cassa1", aspetto));
        Assert.Equal("cfg", (string)preview["cfg"]!["t"]!);
        Assert.Equal("#30343A", (string)preview["cfg"]!["theme"]!["cText"]!);   // la bozza com'è: il display ripiegherebbe
        Assert.Contains(preview["problemi"]!.AsArray(), p => ((string)p!).Contains("poco leggibili"));
        Assert.NotNull(preview["anteprimaQr"]);
        Assert.Empty(preview["cfg"]!["playlists"]!["idle"]!.AsArray());   // il set è spento
        Assert.Null(preview["inOnda"]!["attesa"]);

        var trying = await Json(await agent.Admin.PostAsJsonAsync("/api/bo/aspetto/anteprima?cassa=cassa1&set=set1", aspetto));
        Assert.Equal("promo1", (string)trying["cfg"]!["playlists"]!["sale"]![0]!["id"]!);   // il set che si sta provando, spento o no
        Assert.Equal("Venerdì sera", (string)trying["inOnda"]!["conto"]!);
        Assert.Null(trying["inOnda"]!["grazie"]);
        var stored = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!;
        Assert.Equal("auto", (string)stored["Modo"]!);   // l'anteprima non salva
        Assert.Empty(stored["Set"]!.AsArray());
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
    public async Task Removing_a_cash_point_switches_off_the_sets_that_were_only_for_it()
    {
        await using var agent = await Start(s =>
        {
            AddCash(s, "cassa2", "Bar");
            s.Aspetto.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Colazione", Titolo = "Caffè e cornetto" });
            s.Aspetto.Set.Add(new AdSetConfig { Id = "set1", Nome = "Solo bar", Dove = ["conto"], Casse = ["cassa2"], Elementi = [new() { Id = "promo1" }] });
            s.Aspetto.Set.Add(new AdSetConfig { Id = "set2", Nome = "Tutte e due", Dove = ["conto"], Casse = ["cassa1", "cassa2"], Elementi = [new() { Id = "promo1" }] });
        });
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/casse/cassa2")).StatusCode);
        var sets = (await Json(await agent.Admin.GetAsync("/api/bo/aspetto")))["Aspetto"]!["Set"]!.AsArray();
        Assert.False((bool)sets[0]!["Attivo"]!);   // non passa a tutte le casse
        Assert.Empty(sets[0]!["Casse"]!.AsArray());
        Assert.True((bool)sets[1]!["Attivo"]!);
        Assert.Equal(["cassa1"], sets[1]!["Casse"]!.AsArray().Select(c => (string)c!));
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
                s.Aspetto.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Spritz", Immagine = gone });
                s.Aspetto.Promozioni.Add(new PromoConfig { Id = "promo2", Nome = "Pizza", Titolo = "La pizza del mese", Immagine = gone });
                s.Aspetto.Set.Add(new AdSetConfig { Id = "set1", Nome = "Sempre", Dove = ["attesa"], Elementi = [new() { Id = gone }, new() { Id = "promo1" }, new() { Id = "promo2" }] });
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
            var pizza = Assert.Single(store.Current.Aspetto.Promozioni);   // la pizza ha ancora il titolo
            Assert.Null(pizza.Immagine);
            Assert.Equal(["promo2"], store.Current.Aspetto.Set[0].Elementi.Select(e => e.Id));
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

    [Fact]
    public void The_promotions_of_the_first_version_move_into_the_gallery_and_three_sets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bt-migra-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var photo = new string('a', 20) + ".jpg";
            var video = new string('b', 20) + ".mp4";
            var old = new JsonObject
            {
                ["Casse"] = new JsonArray(new JsonObject { ["Id"] = "cassa1", ["Nome"] = "Cassa 1", ["FileCassa"] = Path.Combine(folder, "cassa1", "Scontrino.txt") }),
                ["Aspetto"] = new JsonObject
                {
                    ["QrGrazie"] = new JsonObject { ["Attivo"] = true, ["Indirizzo"] = "https://example.com/promozioni", ["Titolo"] = "Scopri le promozioni!", ["Didascalia"] = "Inquadra il QR", ["Secondi"] = 45 },
                    ["Promozioni"] = new JsonArray(
                        new JsonObject
                        {
                            ["Id"] = "promo1", ["Nome"] = "Spritz", ["Attiva"] = true, ["NelleSlide"] = true, ["NelConto"] = true, ["Media"] = photo, ["SecondiImmagine"] = 9,
                            ["Titolo"] = "Spritz a 5 €", ["Testo"] = "", ["Casse"] = new JsonArray(), ["Giorni"] = new JsonArray(5), ["Dalle"] = null, ["Alle"] = null, ["AlGiorno"] = "2026-12-31",
                        },
                        new JsonObject { ["Id"] = "promo2", ["Nome"] = "Forno", ["Attiva"] = true, ["NelleSlide"] = true, ["NelConto"] = false, ["Media"] = video, ["SecondiImmagine"] = 7, ["Titolo"] = "", ["Testo"] = "" },
                        new JsonObject { ["Id"] = "promo3", ["Nome"] = "Spenta", ["Attiva"] = false, ["NelleSlide"] = false, ["NelConto"] = true, ["Media"] = null, ["SecondiImmagine"] = 7, ["Titolo"] = "Dolce del giorno", ["Testo"] = "" }),
                    ["Media"] = new JsonArray(
                        new JsonObject { ["Id"] = photo, ["Nome"] = "spritz.jpg", ["Tipo"] = "image/jpeg", ["Byte"] = 1 },
                        new JsonObject { ["Id"] = video, ["Nome"] = "forno.mp4", ["Tipo"] = "video/mp4", ["Byte"] = 1 }),
                },
            };
            File.WriteAllText(Path.Combine(folder, SettingsStore.FileName), old.ToJsonString());

            var store = new SettingsStore(folder);
            var a = store.Current.Aspetto;
            Assert.Equal(["promo1", "promo3", "promo4"], a.Promozioni.Select(p => p.Id));   // il forno era solo un video: ora è nel set
            Assert.Equal(photo, a.Promozioni[0].Immagine);
            Assert.Equal(new DateOnly(2026, 12, 31), a.Promozioni[0].AlGiorno);
            var qr = a.Promozioni[2];
            Assert.Equal(("QR dopo il pagamento", "Scopri le promozioni!", "https://example.com/promozioni", "Inquadra il QR"), (qr.Nome, qr.Titolo, qr.Qr, qr.QrDidascalia));
            Assert.Equal(45, a.SecondiGrazie);
            Assert.Equal(["Attesa", "Accanto al conto", "Grazie"], a.Set.Select(x => x.Nome));
            Assert.Equal(new (string, int?)[] { (photo, 9), (video, null) }, a.Set[0].Elementi.Select(e => (e.Id, e.Secondi)));
            Assert.Equal(["promo1"], a.Set[1].Elementi.Select(e => e.Id));   // la promozione spenta resta nella galleria, fuori dai set
            Assert.Equal(["promo4"], a.Set[2].Elementi.Select(e => e.Id));
            Assert.Null(store.Current.Validate());
            // Il venerdì dello spritz va rimesso sul set: lo dice il log.
            Assert.Contains(store.LoadNotes, n => n.Contains("nei set Attesa, Accanto al conto, Grazie") && n.Contains("controllali dal back office"));

            // Al primo salvataggio il file è nel formato nuovo, e la conversione non si ripete.
            Assert.Null(store.Update(_ => null));
            var saved = File.ReadAllText(Path.Combine(folder, SettingsStore.FileName));
            Assert.DoesNotContain("NelleSlide", saved);
            Assert.DoesNotContain("QrGrazie", saved);
            var again = new SettingsStore(folder);
            Assert.Equal(3, again.Current.Aspetto.Set.Count);
            Assert.DoesNotContain(again.LoadNotes, n => n.Contains("prima versione"));
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
