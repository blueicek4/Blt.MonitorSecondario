using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Aspetto;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Impostazioni;
using Xunit;
using static Bluetech.Display.Agent.Tests.TestAgent;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// Il locale sullo schermo del cliente (iterazione 2): i dieci temi e il contrasto, le vetrine con le regole e i cinque
/// stati (ScegliVetrina), la configurazione già piatta per ogni schermo, le foto e i video dei marchi e dei kit.
/// </summary>
public sealed class AppearanceTests
{
    static AgentSettings WithCash()
    {
        var settings = AgentSettings.Defaults();
        settings.Casse[0].FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "Scontrino.txt");
        return settings;
    }

    static string? Qr(string url) => ScreenConfig.QrPng(url);

    static MediaItem Photo(char c, string name) => new() { Id = new string(c, 20) + ".jpg", Nome = name, Tipo = "image/jpeg" };
    static MediaItem Video(char c, string name) => new() { Id = new string(c, 20) + ".mp4", Nome = name, Tipo = "video/mp4" };

    /// <summary>Le coppie con il rapporto scritto nei commenti di themes.ts: ricalcolate qui, e mai sotto 4,5:1.</summary>
    [Theory]
    [InlineData("bluetech", "text", "bg", 15.82)]
    [InlineData("bluetech", "text", "surfaceSolid", 14.34)]
    [InlineData("bluetech", "muted", "bg", 7.76)]
    [InlineData("bluetech", "muted", "surfaceSolid", 7.03)]
    [InlineData("bluetech", "faint", "bg", 5.12)]
    [InlineData("bluetech", "faint", "surfaceSolid", 4.64)]
    [InlineData("bluetech", "onPrimary", "primary", 4.63)]
    [InlineData("bluetech", "accent", "bg", 10.80)]
    [InlineData("bluetech", "accent", "surfaceSolid", 9.78)]
    [InlineData("brace", "text", "bg", 15.71)]
    [InlineData("brace", "text", "surfaceSolid", 14.59)]
    [InlineData("brace", "muted", "bg", 8.04)]
    [InlineData("brace", "muted", "surfaceSolid", 7.46)]
    [InlineData("brace", "faint", "bg", 5.00)]
    [InlineData("brace", "faint", "surfaceSolid", 4.64)]
    [InlineData("brace", "onPrimary", "primary", 5.08)]
    [InlineData("brace", "accent", "bg", 9.62)]
    [InlineData("brace", "accent", "surfaceSolid", 8.93)]
    [InlineData("brace", "totalText", "totalFill", 5.08)]
    [InlineData("notturno", "text", "bg", 17.06)]
    [InlineData("notturno", "text", "surfaceSolid", 15.67)]
    [InlineData("notturno", "muted", "bg", 7.81)]
    [InlineData("notturno", "muted", "surfaceSolid", 7.18)]
    [InlineData("notturno", "faint", "bg", 5.06)]
    [InlineData("notturno", "faint", "surfaceSolid", 4.65)]
    [InlineData("notturno", "onPrimary", "primary", 6.50)]
    [InlineData("notturno", "accent", "bg", 14.32)]
    [InlineData("notturno", "accent", "surfaceSolid", 13.16)]
    [InlineData("riserva", "text", "bg", 15.83)]
    [InlineData("riserva", "text", "surfaceSolid", 14.81)]
    [InlineData("riserva", "muted", "bg", 6.84)]
    [InlineData("riserva", "muted", "surfaceSolid", 6.40)]
    [InlineData("riserva", "faint", "bg", 4.98)]
    [InlineData("riserva", "faint", "surfaceSolid", 4.66)]
    [InlineData("riserva", "onPrimary", "primary", 8.13)]
    [InlineData("riserva", "accent", "bg", 8.51)]
    [InlineData("riserva", "accent", "surfaceSolid", 7.97)]
    [InlineData("onda", "text", "bg", 16.24)]
    [InlineData("onda", "text", "surfaceSolid", 14.48)]
    [InlineData("onda", "muted", "bg", 8.08)]
    [InlineData("onda", "muted", "surfaceSolid", 7.21)]
    [InlineData("onda", "faint", "bg", 5.21)]
    [InlineData("onda", "faint", "surfaceSolid", 4.65)]
    [InlineData("onda", "onPrimary", "primary", 5.74)]
    [InlineData("onda", "accent", "bg", 10.15)]
    [InlineData("onda", "accent", "surfaceSolid", 9.05)]
    [InlineData("onda", "totalText", "totalFill", 5.74)]
    [InlineData("giorno", "text", "bg", 16.92)]
    [InlineData("giorno", "text", "surfaceSolid", 18.31)]
    [InlineData("giorno", "muted", "bg", 5.37)]
    [InlineData("giorno", "muted", "surfaceSolid", 5.81)]
    [InlineData("giorno", "faint", "bg", 4.63)]
    [InlineData("giorno", "faint", "surfaceSolid", 5.01)]
    [InlineData("giorno", "onPrimary", "primary", 6.61)]
    [InlineData("giorno", "accent", "bg", 4.95)]
    [InlineData("giorno", "accent", "surfaceSolid", 5.35)]
    [InlineData("giorno", "totalText", "totalFill", 6.61)]
    [InlineData("forno", "text", "bg", 13.67)]
    [InlineData("forno", "text", "surfaceSolid", 15.25)]
    [InlineData("forno", "muted", "bg", 5.67)]
    [InlineData("forno", "muted", "surfaceSolid", 6.33)]
    [InlineData("forno", "faint", "bg", 4.66)]
    [InlineData("forno", "faint", "surfaceSolid", 5.20)]
    [InlineData("forno", "onPrimary", "primary", 6.91)]
    [InlineData("forno", "accent", "bg", 5.58)]
    [InlineData("forno", "accent", "surfaceSolid", 6.22)]
    [InlineData("forno", "totalText", "totalFill", 6.91)]
    [InlineData("latte", "text", "bg", 14.79)]
    [InlineData("latte", "text", "surfaceSolid", 15.78)]
    [InlineData("latte", "muted", "bg", 5.62)]
    [InlineData("latte", "muted", "surfaceSolid", 5.99)]
    [InlineData("latte", "faint", "bg", 4.63)]
    [InlineData("latte", "faint", "surfaceSolid", 4.94)]
    [InlineData("latte", "onPrimary", "primary", 6.82)]
    [InlineData("latte", "accent", "bg", 5.77)]
    [InlineData("latte", "accent", "surfaceSolid", 6.16)]
    [InlineData("latte", "totalText", "totalFill", 6.82)]
    [InlineData("sale", "text", "bg", 16.26)]
    [InlineData("sale", "text", "surfaceSolid", 17.57)]
    [InlineData("sale", "muted", "bg", 5.29)]
    [InlineData("sale", "muted", "surfaceSolid", 5.71)]
    [InlineData("sale", "faint", "bg", 4.64)]
    [InlineData("sale", "faint", "surfaceSolid", 5.02)]
    [InlineData("sale", "onPrimary", "primary", 4.91)]
    [InlineData("sale", "accent", "bg", 5.55)]
    [InlineData("sale", "accent", "surfaceSolid", 5.99)]
    [InlineData("menta", "text", "bg", 15.15)]
    [InlineData("menta", "text", "surfaceSolid", 16.42)]
    [InlineData("menta", "muted", "bg", 5.68)]
    [InlineData("menta", "muted", "surfaceSolid", 6.15)]
    [InlineData("menta", "faint", "bg", 4.67)]
    [InlineData("menta", "faint", "surfaceSolid", 5.05)]
    [InlineData("menta", "onPrimary", "primary", 6.48)]
    [InlineData("menta", "accent", "bg", 5.09)]
    [InlineData("menta", "accent", "surfaceSolid", 5.52)]
    [InlineData("menta", "totalText", "totalFill", 6.48)]
    public void Every_contrast_in_the_design_comments_is_recomputed(string theme, string foreground, string background, double expected)
    {
        var palette = Palettes.All[theme];
        string Color(string field) => (string)typeof(Palette).GetProperty(char.ToUpperInvariant(field[0]) + field[1..])!.GetValue(palette)!;
        var ratio = ThemeColors.Contrast(Color(foreground), Color(background));
        Assert.NotNull(ratio);
        Assert.Equal(expected, ratio.Value, 0.01);
        Assert.True(ratio >= ThemeColors.MinContrast, $"{theme}: {foreground} su {background} {ratio:0.00}:1");
    }

    [Fact]
    public void Ten_themes_five_dark_five_light_with_their_twins()
    {
        Assert.Equal(10, Palettes.Themes.Count);
        Assert.Equal(5, Palettes.Themes.Count(t => t.Scuro));
        foreach (var theme in Palettes.Themes)
        {
            Assert.All(ThemeColors.Pairs(theme), pair => Assert.True(pair.Ok, $"{theme.Id}: {pair.Label} {pair.Ratio}"));
            Assert.Equal(theme.Scuro ? "light" : "dark", theme.FooterVariant);   // la firma chiara sui fondi scuri
            if (theme.Gemello is { } twin)
            {
                Assert.Equal(theme.Id, Palettes.All[twin].Gemello);   // i gemelli si scambiano tra loro
                Assert.NotEqual(theme.Scuro, Palettes.All[twin].Scuro);
            }
            Assert.True(Palettes.Fonts.ContainsKey(Palettes.FamilyOf(theme.FontDisplay)));
            Assert.True(Palettes.Fonts.ContainsKey(Palettes.FamilyOf(theme.FontText)));
        }
        Assert.Equal(["latte", "notturno"], Palettes.Themes.Where(t => t.Gemello == null).Select(t => t.Id).Order());
        Assert.Equal(14, Palettes.Fonts.Count);

        // Giorno e notte automatici: di giorno il gemello chiaro, di sera quello scuro. Notturno e Latte restano.
        Assert.Equal("forno", Palettes.Resolve("brace", "auto", daytime: true).Id);
        Assert.Equal("brace", Palettes.Resolve("brace", "auto", daytime: false).Id);
        Assert.Equal("brace", Palettes.Resolve("forno", "auto", daytime: false).Id);
        Assert.Equal("brace", Palettes.Resolve("brace", "fisso", daytime: true).Id);
        Assert.Equal("notturno", Palettes.Resolve("notturno", "auto", daytime: true).Id);
        Assert.Equal("bluetech", Palettes.Get("rococò").Id);
        Assert.Equal("sale", Palettes.Legacy["fine-giorno"]);
    }

    [Fact]
    public void Locale_colors_recalculate_what_the_theme_derives_from_them()
    {
        var day = Palettes.Get("giorno");
        var colors = LocaleColors.Of(day) with { Primary = "#B3261E", Text = "#1B1B1B" };
        var palette = ThemeColors.WithLocaleColors(day, colors);
        Assert.Equal("#B3261E", palette.TotalFill);   // il blocco del totale era il primario
        Assert.Equal("rgba(27,27,27,0.1)", palette.Line);   // i filetti seguono il testo, con la stessa trasparenza
        Assert.Equal("#B3261E", palette.Glow);

        var night = Palettes.Get("bluetech");
        var dark = ThemeColors.WithLocaleColors(night, LocaleColors.Of(night) with { Bg = "#202020" });
        Assert.Equal(ThemeColors.Composite(night.Surface, "#202020"), dark.SurfaceSolid);   // la card opaca sul fondo nuovo
    }

    [Fact]
    public void Unreadable_colors_are_refused_with_the_nearest_fix()
    {
        var settings = WithCash();
        var look = settings.Marchi[0].Aspetto;
        look.Tema = "giorno";
        var day = Palettes.Get("giorno");
        look.Colori = new ThemeColorSettings
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
        look.Colori.Testo = fix;
        Assert.Null(settings.Validate());   // la correzione proposta passa
        Assert.InRange(ThemeColors.Contrast(fix, "#FFFFFF")!.Value, 4.5, 5.2);   // ed è la più vicina, non il nero

        look.Colori.SulPrimario = "#3A6FD0";   // blu su blu
        Assert.Contains("testo sopra il primario #", settings.Validate());

        look.Modo = "auto";   // con giorno e notte valgono i colori del tema, come nel design
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void The_rest_of_the_brand_is_checked_in_words()
    {
        var settings = WithCash();
        var brand = settings.Marchi[0];
        brand.Aspetto.Tema = "rococò";
        Assert.Contains("Il locale: il tema \"rococò\" non c'è. Si sceglie tra Bluetech, Brace", settings.Validate());
        brand.Aspetto.Tema = "riserva";
        brand.Aspetto.FontTitoli = "Comic Sans";
        Assert.Contains("font dei titoli", settings.Validate());
        brand.Aspetto.FontTitoli = "Oswald";
        brand.Aspetto.Modo = "auto";
        brand.Aspetto.GiornoDalle = "19:00";
        brand.Aspetto.GiornoAlle = "07:00";
        Assert.Contains("il giorno va dalle", settings.Validate());
        brand.Aspetto.GiornoDalle = "07:30";
        brand.Aspetto.GiornoAlle = "20:00";
        brand.Aspetto.Fondo = "immagine";
        Assert.Contains("sfondo a immagine", settings.Validate());
        brand.Aspetto.Fondo = "mesh";
        brand.Schermo.ColonneConto = 18;
        Assert.Contains("da 9 a 17 colonne su 24", settings.Validate());
        brand.Schermo.ColonneConto = 14;
        brand.Schermo.Attrazione = "esagerato";
        Assert.Contains("sobrio, vivo, spettacolo o troppo", settings.Validate());
        brand.Schermo.Attrazione = "troppo";

        var c = brand.Contenuti;
        c.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Registrati" });
        Assert.Contains("La promozione Registrati: scrivi un titolo o un testo, o scegli un'immagine o un QR", settings.Validate());
        c.Promozioni[0].Titolo = "Diventa cliente";
        c.Promozioni[0].Modello = "D";
        Assert.Contains("il modello D è quello con il QR", settings.Validate());
        c.Promozioni[0].Qr = "javascript:alert(1)";
        Assert.Contains("l'indirizzo del QR è come https://", settings.Validate());
        c.Promozioni[0].Qr = "https://example.com/registrati";
        c.Promozioni[0].Etichetta = "offerta lampo";
        Assert.Contains("l'etichetta si sceglie tra", settings.Validate());
        c.Promozioni[0].Etichetta = "cliente";
        c.Promozioni.Add(new PromoConfig { Id = "promo2", Nome = "Pizza", Titolo = "La pizza del mese", Modello = "C" });
        Assert.Contains("i modelli B e C sono con la foto", settings.Validate());
        c.Promozioni[1].Modello = "A";

        c.Regole.Add(new RuleConfig { Id = "regola1", Nome = "Venerdì sera", Giorni = [5], Dalle = "18:00" });
        Assert.Contains("gli orari sono come 18:00", settings.Validate());
        c.Regole[0].Alle = "02:00";
        c.Regole[0].Casse = ["cassa9"];
        Assert.Contains("la cassa cassa9 non c'è", settings.Validate());
        c.Regole[0].Casse = ["cassa1"];
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina1", Nome = "Aperitivo", Regola = "regola1", Elementi = [new() { Id = "promo1" }] });
        Assert.Contains("La vetrina Aperitivo: scegli dove si vede", settings.Validate());
        c.Vetrine[0].Posti = ["attesa"];
        c.Vetrine[0].Elementi.Add(new ShowcaseItemConfig { Id = "promo9" });
        Assert.Contains("un elemento non c'è più (promo9)", settings.Validate());
        c.Vetrine[0].Elementi.RemoveAt(1);
        c.Vetrine[0].Rotazione = "a caso";
        Assert.Contains("l'ordine è elenco, casuale o alfabetico", settings.Validate());
        c.Vetrine[0].Rotazione = "casuale";
        c.Vetrine[0].QrOgni = 2;

        var receipt = brand.Vendita.ScontrinoDigitale;
        receipt.Blocchi = [new() { Tipo = "carta", Attivo = true, Colonna = 1, Riga = "alto" }];
        Assert.Contains("la carta occupa sempre tutta l'altezza", settings.Validate());
        receipt.Blocchi = [new() { Tipo = "qr", Attivo = true, Colonna = 2, Riga = "piena" }, new() { Tipo = "reclame", Attivo = true, Colonna = 2, Riga = "alto" }];
        Assert.Contains("nella colonna 2 due blocchi stanno solo uno in alto e uno in basso", settings.Validate());
        receipt.Blocchi = [new() { Tipo = "qr", Attivo = false, Colonna = 2, Riga = "piena" }];
        Assert.Contains("accendi almeno un blocco", settings.Validate());
        receipt.Blocchi = [new() { Tipo = "qr", Attivo = true, Colonna = 2, Riga = "alto" }, new() { Tipo = "reclame", Attivo = true, Colonna = 2, Riga = "basso" }];

        settings.Installazione.Media.Add(Video('b', "forno.mp4"));
        settings.Installazione.Loghi.Satispay = settings.Installazione.Media[0].Id;
        Assert.Contains("Marchi dei kit: il logo Satispay è un'immagine", settings.Validate());
        settings.Installazione.Loghi.Satispay = null;
        Assert.Null(settings.Validate());

        // Con due marchi ogni errore dice di quale.
        settings.Marchi.Add(new BrandConfig { Id = "bar", Nome = "Il bar", Aspetto = { Tema = "latte", Raggio = 40 } });
        Assert.Contains("Il bar: Il locale: gli angoli arrotondati vanno da 0 a 32 px.", settings.Validate());
    }

    static (BrandConfig Brand, AgentSettings Settings) Showcased()
    {
        var settings = WithCash();
        settings.Casse.Add(new CashPointConfig { Id = "cassa2", Nome = "Bar", Marchio = "locale", FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "bar", "Scontrino.txt") });
        var brand = settings.Marchi[0];
        var c = brand.Contenuti;
        c.Media.AddRange([Photo('a', "spritz.jpg"), Photo('b', "tagliere.jpg"), Photo('c', "pizza.jpg")]);
        c.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Ottobre", Titolo = "Castagne e vin brulé", AlGiorno = new DateOnly(2026, 10, 31) });
        c.Regole.Add(new RuleConfig { Id = "regola1", Nome = "Aperitivo", Dalle = "18:00", Alle = "20:00" });
        c.Regole.Add(new RuleConfig { Id = "regola2", Nome = "Solo bar", Casse = ["cassa2"] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina1", Nome = "Aperitivo", Posti = ["attesa"], Regola = "regola1", Elementi = [new() { Id = c.Media[0].Id }, new() { Id = c.Media[1].Id }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina2", Nome = "Solo ottobre", Posti = ["conto"], Elementi = [new() { Id = "promo1" }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina3", Nome = "Sempre", Posti = ["attesa", "conto"], Elementi = [new() { Id = c.Media[2].Id }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina4", Nome = "Spenta", Attiva = false, Posti = ["grazie"], Elementi = [new() { Id = "promo1" }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina5", Nome = "Bar", Posti = ["grazie"], Regola = "regola2", Elementi = [new() { Id = c.Media[1].Id }] });
        Assert.Null(settings.Validate());
        return (brand, settings);
    }

    [Fact]
    public void For_each_place_the_first_valid_showcase_wins_and_the_others_say_why()
    {
        var (brand, settings) = Showcased();
        var aperitivo = new DateTime(2026, 10, 20, 19, 0, 0);   // martedì
        var idle = Showcases.Choose(brand, "cassa1", "attesa", aperitivo);
        Assert.Equal("vetrina1", idle.Winner!.Id);
        Assert.Equal([brand.Contenuti.Media[0].Id, brand.Contenuti.Media[1].Id], idle.Items.Select(i => i.Id));
        Assert.Equal(Showcases.Covered, idle.States["vetrina3"].Stato);   // Sempre vale, ma vince quella sopra
        Assert.Equal("vetrina1", idle.States["vetrina3"].Da);
        Assert.Equal(["vetrina1", "vetrina3"], idle.States.Keys);   // 1. solo quelle dell'attesa

        var thanks = Showcases.Choose(brand, "cassa1", "grazie", aperitivo);
        Assert.Null(thanks.Winner);
        Assert.Equal(Showcases.Off, thanks.States["vetrina4"].Stato);
        Assert.False(thanks.States.ContainsKey("vetrina5"));   // 2. per la cassa 1 la vetrina del bar non esiste
        Assert.Equal("vetrina5", Showcases.Choose(brand, "cassa2", "grazie", aperitivo).Winner!.Id);

        var evening = new DateTime(2026, 10, 20, 21, 0, 0);
        var late = Showcases.Choose(brand, "cassa1", "attesa", evening);
        Assert.Equal("vetrina3", late.Winner!.Id);
        Assert.Equal(Showcases.OffHours, late.States["vetrina1"].Stato);
        Assert.Equal(new DateTime(2026, 10, 21, 18, 0, 0), late.States["vetrina1"].Torna);   // torna domani alle 18
        Assert.Equal("domani alle 18:00", Showcases.When(late.States["vetrina1"].Torna!.Value, evening));

        // 3. A novembre la promozione di ottobre è scaduta: la vetrina è vuota e va in onda quella dopo.
        var november = new DateTime(2026, 11, 2, 12, 0, 0);
        var sale = Showcases.Choose(brand, "cassa1", "conto", november);
        Assert.Equal("vetrina3", sale.Winner!.Id);
        Assert.Equal(Showcases.Empty, sale.States["vetrina2"].Stato);

        // L'anteprima mostra una vetrina nei suoi posti anche spenta e fuori orario.
        Assert.Equal("vetrina4", Showcases.Choose(brand, "cassa1", "grazie", november, force: "vetrina4").Winner!.Id);

        // Il pannello: lo stato di ogni vetrina su tutte le casse e tutti i posti.
        var overview = Showcases.Overviews(settings, brand, aperitivo);
        Assert.Equal(Showcases.OnAir, overview["vetrina1"].Stato);
        Assert.Equal([("cassa1", "attesa"), ("cassa2", "attesa")], overview["vetrina1"].Dove);
        Assert.Equal(Showcases.Covered, overview["vetrina3"].Stato);   // nell'attesa la copre l'aperitivo, accanto al conto ottobre
        Assert.Equal(["vetrina1", "vetrina2"], overview["vetrina3"].CopertaDa.Order());
        Assert.Equal(Showcases.Off, overview["vetrina4"].Stato);
        var night = Showcases.Overviews(settings, brand, november.AddHours(10));
        Assert.Equal(Showcases.OffHours, night["vetrina1"].Stato);
        Assert.Equal(Showcases.Empty, night["vetrina2"].Stato);
    }

    [Fact]
    public void The_week_on_air_is_the_same_choice_the_screens_make()
    {
        var (brand, _) = Showcased();
        var monday = new DateOnly(2026, 10, 19);
        var week = Showcases.Week(brand, "cassa1", "attesa", monday);
        Assert.Equal(7, week.Count);
        // I minuti partono dalle 06:00: l'aperitivo 18-20 è 720-840, il resto della giornata è di Sempre.
        Assert.All(week, day => Assert.Equal(new Showcases.Span[] { new(0, 720, "vetrina3"), new(720, 840, "vetrina1"), new(840, 1440, "vetrina3") }, day));

        // Una serata che passa la mezzanotte appartiene al giorno in cui comincia: il venerdì fino all'01:00, il sabato no.
        var rule = brand.Contenuti.Regole[0];
        rule.Giorni = [5];
        rule.Dalle = "21:00";
        rule.Alle = "01:00";
        week = Showcases.Week(brand, "cassa1", "attesa", monday);
        Assert.Equal(new Showcases.Span[] { new(0, 900, "vetrina3"), new(900, 1140, "vetrina1"), new(1140, 1440, "vetrina3") }, week[4]);
        Assert.Equal(new Showcases.Span[] { new(0, 1440, "vetrina3") }, week[5]);
        Assert.Equal(new Showcases.Span[] { new(0, 1440, "vetrina5") }, Showcases.Week(brand, "cassa2", "grazie", monday)[0]);   // la vetrina del bar
        Assert.Equal(new Showcases.Span[] { new(0, 1440, null) }, Showcases.Week(brand, "cassa1", "grazie", monday)[0]);   // per la sala, niente
    }

    [Fact]
    public void A_rule_follows_its_cash_points_days_hours_and_dates()
    {
        var friday = new RuleConfig { Giorni = [5], Dalle = "22:00", Alle = "02:00", Casse = ["cassa2"] };
        var fridayNight = new DateTime(2026, 10, 23, 23, 30, 0);   // venerdì
        Assert.NotNull(Showcases.ServiceDay(friday, "cassa2", fridayNight));
        Assert.Null(Showcases.ServiceDay(friday, "cassa1", fridayNight));   // un'altra cassa
        Assert.Equal(new DateOnly(2026, 10, 23), Showcases.ServiceDay(friday, "cassa2", fridayNight.AddHours(2)));   // sabato all'1:30 è ancora venerdì sera
        Assert.Null(Showcases.ServiceDay(friday, "cassa2", fridayNight.AddHours(3)));   // le 2:30: finita
        Assert.Null(Showcases.ServiceDay(friday, "cassa2", fridayNight.AddDays(1)));   // sabato sera no
        Assert.Equal(new DateTime(2026, 10, 30, 22, 0, 0), Showcases.NextStart(friday, "cassa2", fridayNight.AddHours(3)));
        Assert.Null(Showcases.NextStart(friday, "cassa1", fridayNight));

        var october = new RuleConfig { DalGiorno = new DateOnly(2026, 10, 1), AlGiorno = new DateOnly(2026, 10, 31) };
        Assert.NotNull(Showcases.ServiceDay(october, "cassa1", new DateTime(2026, 10, 31, 23, 59, 0)));
        Assert.Null(Showcases.ServiceDay(october, "cassa1", new DateTime(2026, 11, 1, 0, 1, 0)));
        Assert.Null(Showcases.NextStart(october, "cassa1", new DateTime(2026, 11, 1, 0, 1, 0)));   // non torna più
        Assert.Equal(new DateTime(2026, 10, 1), Showcases.NextStart(october, "cassa1", new DateTime(2026, 9, 20, 12, 0, 0)));
        Assert.NotNull(Showcases.ServiceDay(null, "cassa1", fridayNight));   // senza regola: sempre
    }

    static List<Showcases.Entry> Entries(params (string Id, bool Qr)[] items) =>
        items.Select(i => new Showcases.Entry(i.Id, i.Id, i.Qr ? null : Photo('a', i.Id), i.Qr ? new PromoConfig { Id = i.Id, Qr = "https://example.com/" + i.Id } : null, 8)).ToList();

    [Fact]
    public void In_order_by_name_or_at_random_with_a_qr_promotion_after_every_few()
    {
        var items = Entries(("cantina", false), ("aperitivo", false), ("brunch", false), ("registrati", true), ("recensioni", true));
        var inOrder = Showcases.Sequence(new ShowcaseConfig { Id = "vetrina1", Rotazione = "elenco" }, items);
        Assert.Equal(["cantina", "aperitivo", "brunch", "registrati", "recensioni"], inOrder.Select(i => i.Id));   // lo schermo la ripete
        var byName = Showcases.Sequence(new ShowcaseConfig { Id = "vetrina1", Rotazione = "alfabetico" }, items);
        Assert.Equal(["aperitivo", "brunch", "cantina", "recensioni", "registrati"], byName.Select(i => i.Id));

        // In ordine con un QR dopo ogni foto: le due liste fanno giri interi (3 foto e 2 QR: 6 blocchi).
        var everyOther = Showcases.Sequence(new ShowcaseConfig { Id = "vetrina1", Rotazione = "elenco", QrOgni = 1 }, items);
        Assert.Equal(["cantina", "registrati", "aperitivo", "recensioni", "brunch", "registrati", "cantina", "recensioni", "aperitivo", "registrati", "brunch", "recensioni"],
            everyOther.Select(i => i.Id));

        // A caso, due foto e un QR: "due slide a caso, un QR, due slide, un altro QR".
        var showcase = new ShowcaseConfig { Id = "vetrina7", Rotazione = "casuale", QrOgni = 2 };
        var random = Showcases.Sequence(showcase, items);
        Assert.InRange(random.Count, 60, 300);
        Assert.Equal(0, random.Count % 3);
        for (var i = 0; i < random.Count; i++)
            Assert.Equal(i % 3 == 2, random[i].HasQr);
        Assert.Equal(3, random.Where(i => !i.HasQr).Select(i => i.Id).Distinct().Count());
        Assert.Single(random.Where(i => !i.HasQr).GroupBy(i => i.Id).Select(g => g.Count()).Distinct());   // ognuna lo stesso numero di volte
        Assert.Single(random.Where(i => i.HasQr).GroupBy(i => i.Id).Select(g => g.Count()).Distinct());
        Assert.Equal(random.Select(i => i.Id), Showcases.Sequence(showcase, items).Select(i => i.Id));   // sempre la stessa: il cfg non cambia a ogni controllo
        Assert.NotEqual(random.Select(i => i.Id), Showcases.Sequence(new ShowcaseConfig { Id = "vetrina8", Rotazione = "casuale", QrOgni = 2 }, items).Select(i => i.Id));

        // A caso senza la regola: giri mescolati, mai lo stesso elemento due volte di fila.
        var shuffled = Showcases.Sequence(new ShowcaseConfig { Id = "vetrina2", Rotazione = "casuale" }, items);
        Assert.Equal(60, shuffled.Count);
        Assert.All(Enumerable.Range(1, shuffled.Count - 1), i => Assert.NotEqual(shuffled[i - 1].Id, shuffled[i].Id));
        Assert.All(shuffled.Chunk(5), round => Assert.Equal(5, round.Select(i => i.Id).Distinct().Count()));

        // Solo promozioni con il QR, o nessuna: la regola non c'entra.
        Assert.Equal(["cantina", "aperitivo"], Showcases.Sequence(new ShowcaseConfig { Rotazione = "elenco", QrOgni = 2 }, items.Take(2).ToList()).Select(i => i.Id));
        Assert.True(Showcases.Sequence(new ShowcaseConfig { Rotazione = "casuale", QrOgni = 3 }, items.Skip(3).ToList()).All(i => i.HasQr));
    }

    [Fact]
    public void The_config_is_flat_per_screen_with_the_brand_the_cash_point_and_the_screen()
    {
        var settings = WithCash();
        settings.Casse.Add(new CashPointConfig
        {
            Id = "cassa2", Nome = "Bar", Marchio = "locale", FileCassa = Path.Combine(Path.GetTempPath(), "bt-aspetto", "bar", "Scontrino.txt"),
            Override = new CashOverride { Scritte = new TextsOverride { Benvenuto = "Benvenuti al bar", Scorrevole = "Caffè e cornetto 2 €" } },
        });
        var defaults = ScreenConfig.Build(settings, "cassa1", DateTime.Now, Qr);
        var screen = defaults["screen"]!;
        Assert.Equal(("bluetech", "fisso", "mesh"), ((string)screen["tema"]!, (string)screen["modo"]!, (string)screen["fondo"]!));
        Assert.Equal(("vivo", "light", 16, 10, "basso", "beside"), ((string)screen["attrazione"]!, (string)screen["animazioni"]!, (int)screen["colonneConto"]!,
            (int)screen["colonneContoPagamento"]!, (string)screen["colonnaAllineamento"]!, (string)screen["qrPagamento"]!));   // i valori di oggi
        Assert.Null(screen["colori"]);
        Assert.Null(screen["footerVariant"]);   // la sceglie il tema
        Assert.Equal("Il tuo conto", (string)screen["scritte"]!["titoloConto"]!);
        Assert.Equal(30, (int)screen["scritte"]!["secondiGrazie"]!);
        var receipt = screen["receipt"]!;
        Assert.False((bool)receipt["attivo"]!);
        Assert.Equal(["carta", "qr", "reclame"], receipt["blocchi"]!.AsArray().Select(b => (string)b!["tipo"]!));
        Assert.Equal(45, (int)receipt["secondi"]!);
        Assert.Empty(defaults["showcase"]!.AsObject());
        Assert.Empty(defaults["marks"]!.AsObject());   // senza i loghi dei kit: il marchio di serie, la scritta di Satispay
        Assert.Null(defaults["ticker"]);

        var brand = settings.Marchi[0];
        var c = brand.Contenuti;
        brand.Aspetto.Tema = "forno";
        brand.Aspetto.FontTitoli = "Lora";
        brand.Aspetto.Raggio = 8;
        brand.Aspetto.Vetro = 20;
        brand.Aspetto.Footer = "light";
        brand.Schermo.Animazioni = "eco";
        brand.Schermo.Attrazione = "spettacolo";
        brand.Schermo.QrPagamento = "fullscreen";
        brand.Schermo.PrezziSoloSulConto = true;
        brand.Identita.NomeLocale = "La Vela";
        brand.Scritte.Scorrevole = "Aperitivo dalle 18";
        brand.Scritte.SecondiGrazie = 40;
        brand.Vendita.ScontrinoDigitale.Attivo = true;
        c.Media.Add(Photo('a', "spritz.jpg"));
        c.Media.Add(Video('b', "forno.mp4"));
        c.Media.Add(new MediaItem { Id = new string('f', 20) + ".png", Nome = "logo-scuro.png", Tipo = "image/png" });
        c.Media.Add(new MediaItem { Id = new string('d', 20) + ".jpg", Nome = "forno-primo.jpg", Tipo = "image/jpeg", Derivato = true });
        c.Media[0].Focus = new FocusPoint { X = 0.3, Y = 0.7 };
        c.Media[0].HaScritte = true;
        c.Media[1].Poster = c.Media[3].Id;
        brand.Identita.LogoChiaro = c.Media[2].Id;
        settings.Installazione.Media.Add(new MediaItem { Id = new string('e', 20) + ".png", Nome = "bluetech-chiaro.png", Tipo = "image/png" });
        settings.Installazione.Loghi.BluetechChiaro = settings.Installazione.Media[0].Id;
        c.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Spritz", Immagine = c.Media[0].Id, Titolo = "Spritz a 5 €", Modello = "B", Etichetta = "settimana" });
        c.Promozioni.Add(new PromoConfig { Id = "promo2", Nome = "Registrati", Titolo = "Diventa cliente", Qr = "https://example.com/registrati", QrDidascalia = "Inquadra e registrati", Modello = "D", Etichetta = "nessuna" });
        c.Regole.Add(new RuleConfig { Id = "regola1", Nome = "Solo bar", Casse = ["cassa2"] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina1", Nome = "Solo bar", Posti = ["attesa"], Regola = "regola1", Elementi = [new() { Id = c.Media[1].Id }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina2", Nome = "Sempre", Posti = ["attesa", "conto"], Secondi = 9, Elementi = [new() { Id = c.Media[0].Id }, new() { Id = c.Media[1].Id }, new() { Id = "promo1", Secondi = 12 }] });
        c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina3", Nome = "Grazie", Posti = ["grazie"], Elementi = [new() { Id = "promo2" }] });
        Assert.Null(settings.Validate());

        var config = ScreenConfig.Build(settings, "cassa1", DateTime.Now, Qr);
        screen = config["screen"]!;
        Assert.Equal("forno", (string)screen["tema"]!);
        Assert.Equal("Lora", (string)screen["fontTitoli"]!);
        Assert.Equal((8, 20, "light"), ((int)screen["raggio"]!, (int)screen["vetro"]!, (string)screen["footerVariant"]!));
        Assert.Equal(("eco", "spettacolo", "fullscreen"), ((string)screen["animazioni"]!, (string)screen["attrazione"]!, (string)screen["qrPagamento"]!));
        Assert.True((bool)screen["prezziSoloSulConto"]!);
        Assert.Equal("La Vela", (string)screen["identita"]!["nomeLocale"]!);
        Assert.Null(screen["identita"]!["logo"]);
        Assert.Equal("/media/" + c.Media[2].Id, (string)screen["identita"]!["logoChiaro"]!);
        Assert.True((bool)screen["receipt"]!["attivo"]!);
        Assert.Equal(40, (int)screen["scritte"]!["secondiGrazie"]!);
        Assert.Equal(["bluetechLight"], config["marks"]!.AsObject().Select(m => m.Key));   // solo quelli caricati
        Assert.Equal("Aperitivo dalle 18", (string)config["ticker"]!);

        var idle = config["showcase"]!["attesa"]!;
        Assert.Equal(("vetrina2", 0), ((string)idle["vetrinaId"]!, (int)idle["qrOgni"]!));   // la vetrina del bar no
        Assert.Equal([c.Media[0].Id, c.Media[1].Id, "promo1"], idle["sequenza"]!.AsArray().Select(i => (string)i!));
        var elements = idle["elementi"]!.AsArray();
        Assert.Equal(["foto", "video", "promozione"], elements.Select(e => (string)e!["tipo"]!));
        Assert.Equal(9000, (int)elements[0]!["durataMs"]!);   // i secondi della vetrina
        Assert.Equal("/media/" + c.Media[0].Id, (string)elements[0]!["media"]!["src"]!);
        Assert.Equal(0.3, (double)elements[0]!["media"]!["focus"]!["x"]!);
        Assert.True((bool)elements[0]!["media"]!["scritte"]!);   // ha già le scritte: fasce, non ritaglio
        Assert.Null(elements[1]!["durataMs"]);   // un video dura quanto dura
        Assert.Equal("/media/" + c.Media[3].Id, (string)elements[1]!["media"]!["poster"]!);
        Assert.Equal(12000, (int)elements[2]!["durataMs"]!);   // i secondi dell'elemento
        Assert.Equal(("B", "settimana", "Spritz a 5 €"), ((string)elements[2]!["modello"]!, (string)elements[2]!["etichetta"]!, (string)elements[2]!["titolo"]!));
        Assert.Equal(idle.ToJsonString(), config["showcase"]!["conto"]!.ToJsonString());   // la stessa vetrina accanto al conto
        var thanks = config["showcase"]!["grazie"]!["elementi"]![0]!;
        Assert.Equal(("D", 15000), ((string)thanks["modello"]!, (int)thanks["durataMs"]!));   // un QR resta almeno 15 s
        Assert.Null(thanks["etichetta"]);
        Assert.Equal("Inquadra e registrati", (string)thanks["qr"]!["didascalia"]!);
        var png = Convert.FromBase64String(((string)thanks["qr"]!["png"]!)["data:image/png;base64,".Length..]);
        var image = QrImage.Decode(png)!;
        Assert.Equal(image.Width, image.Height);
        Assert.Equal(0, (image.Width - 21) % 4);   // 21, 25, 29…: un pixel per modulo, senza quiet zone
        Assert.True(image.Dark[0] && image.Dark[6] && !image.Dark[7]);   // il finder pattern in alto a sinistra

        // Alla cassa del bar vince la vetrina più in alto, e valgono le sue scritte.
        var bar = ScreenConfig.Build(settings, "cassa2", DateTime.Now, Qr);
        Assert.Equal([c.Media[1].Id], bar["showcase"]!["attesa"]!["sequenza"]!.AsArray().Select(i => (string)i!));
        Assert.Equal("Benvenuti al bar", (string)bar["screen"]!["scritte"]!["benvenuto"]!);
        Assert.Equal("Caffè e cornetto 2 €", (string)bar["ticker"]!);

        // Lo schermo: le sue eccezioni sopra quelle del marchio, e la copia di carta solo con la stampante.
        brand.Vendita.ScontrinoDigitale.CopiaCarta = true;
        config = ScreenConfig.Build(settings, "cassa1", DateTime.Now, Qr);
        var ipad = ScreenConfig.ForScreen(config, new ScreenOverride { Disposizione = "wide", Attrazione = "troppo", ColonneConto = 30 }, canPrint: false)["screen"]!;
        Assert.Equal(("wide", "troppo", 17), ((string)ipad["disposizione"]!, (string)ipad["attrazione"]!, (int)ipad["colonneConto"]!));
        Assert.False((bool)ipad["receipt"]!["copiaCarta"]!);
        Assert.True((bool)ScreenConfig.ForScreen(config, null, canPrint: true)["screen"]!["receipt"]!["copiaCarta"]!);
        Assert.Null(config["screen"]!["disposizione"]);   // il cfg della cassa resta com'era

        // L'anteprima di una promozione la mette da sola in tutti e tre i posti.
        var preview = ScreenConfig.Build(settings, "cassa1", DateTime.Now, Qr, new ScreenConfig.Preview(Promo: "promo2"));
        Assert.All(ShowcaseConfig.Places, place => Assert.Equal(["promo2"], preview["showcase"]![place]!["sequenza"]!.AsArray().Select(i => (string)i!)));
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
    static byte[] SamplePng(string text = "https://example.com/logo") => Convert.FromBase64String(ScreenConfig.QrPng(text)["data:image/png;base64,".Length..]);

    static async Task<JsonObject> Upload(HttpClient admin, byte[] bytes, string name, string target = "marchio=locale", string type = "image/png")
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return await Json(await admin.PostAsync($"/api/bo/media?{target}&nome=" + Uri.EscapeDataString(name), content));
    }

    static async Task<JsonObject> Config(TestAgent agent) => JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/config"))!.AsObject();

    [Fact]
    public async Task Images_and_videos_are_uploaded_shown_only_to_screens_and_removed_when_unused()
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
        Assert.Contains("non c'è più", (string)(await Upload(agent.Admin, SamplePng(), "x.png", "marchio=nessuno"))["errore"]!);
        Assert.Single(Directory.GetFiles(Path.Combine(agent.DataFolder, "media")));   // niente avanzi del caricamento

        // Lo stesso file come logo del kit: un elenco in più, un file solo sul disco.
        Assert.Equal(id, (string)(await Upload(agent.Admin, SamplePng(), "bluetech.png", "kit=1"))["media"]!["Id"]!);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/installazione/loghi", new { Bluetech = id })).StatusCode);

        var device = await agent.Pair("Tablet");
        var served = await device.Http.GetAsync("/media/" + id);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Contains("immutable", served.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await agent.NewBrowser().Http.GetAsync("/media/" + id)).StatusCode);   // chi non è autorizzato non vede niente
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.GetAsync("/media/" + id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.Http.GetAsync("/media/..%2Fimpostazioni.json")).StatusCode);

        var identity = new { Identita = new { NomeLocale = "La Vela", Logo = id } };
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/marchi/locale", identity)).StatusCode);
        var inUse = await Json(await agent.Admin.DeleteAsync("/api/bo/media/" + id + "?marchio=locale"));
        Assert.Contains("lo usa il logo", (string)inUse["errore"]!);
        Assert.Contains("è il logo Bluetech", (string)(await Json(await agent.Admin.DeleteAsync("/api/bo/media/" + id + "?kit=1")))["errore"]!);

        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/marchi/locale", new { Identita = new { NomeLocale = "La Vela" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/media/" + id + "?marchio=locale")).StatusCode);
        Assert.True(File.Exists(Path.Combine(agent.DataFolder, "media", id)));   // lo tiene ancora il kit
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/installazione/loghi", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/media/" + id + "?kit=1")).StatusCode);
        Assert.False(File.Exists(Path.Combine(agent.DataFolder, "media", id)));
        Assert.Equal(HttpStatusCode.NotFound, (await device.Http.GetAsync("/media/" + id)).StatusCode);
    }

    [Fact]
    public async Task A_video_brings_its_first_frame_made_by_the_back_office()
    {
        await using var agent = await Start();
        var mp4 = new byte[64];
        new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D }.CopyTo(mp4, 0);
        var video = (string)(await Upload(agent.Admin, mp4, "forno.mp4", type: "video/mp4"))["media"]!["Id"]!;
        var frame = (string)(await Upload(agent.Admin, SamplePng("primo"), "forno.png", $"marchio=locale&poster={video}"))["media"]!["Id"]!;
        var blurred = (string)(await Upload(agent.Admin, SamplePng("sfocato"), "forno-sfocato.png", $"marchio=locale&poster={video}&sfocato=1"))["media"]!["Id"]!;
        var media = (await Config(agent))["Marchi"]![0]!["Contenuti"]!["Media"]!.AsArray();
        var item = media.Single(m => (string)m!["Id"]! == video)!;
        Assert.Equal((frame, blurred), ((string)item["Poster"]!, (string)item["PosterSfocato"]!));
        Assert.True((bool)media.Single(m => (string)m!["Id"]! == frame)!["Derivato"]!);   // non compare nella galleria

        Assert.Contains("lo usa il video forno.mp4", (string)(await Json(await agent.Admin.DeleteAsync($"/api/bo/media/{frame}?marchio=locale")))["errore"]!);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync($"/api/bo/media/{video}?marchio=locale")).StatusCode);
        Assert.Empty((await Config(agent))["Marchi"]![0]!["Contenuti"]!["Media"]!.AsArray());   // con il video se ne vanno i suoi fotogrammi
        Assert.Empty(Directory.GetFiles(Path.Combine(agent.DataFolder, "media")));
    }

    [Fact]
    public async Task A_change_to_the_brand_reaches_the_screens_at_once()
    {
        await using var agent = await Start();
        var device = await agent.Pair("Player cassa");
        using var socket = await agent.Connect(device);
        Assert.Equal("bluetech", (string)(await Receive(socket, Type("cfg")))["screen"]!["tema"]!);
        await Receive(socket, Type("sync"));

        var uploaded = (string)(await Upload(agent.Admin, SamplePng(), "aperitivo.png"))["media"]!["Id"]!;
        var brand = new JsonObject
        {
            ["Identita"] = new JsonObject { ["NomeLocale"] = "Bar Centrale" },
            ["Aspetto"] = new JsonObject { ["Tema"] = "notturno", ["Modo"] = "fisso" },
            ["Scritte"] = new JsonObject { ["Benvenuto"] = "Benvenuti", ["Scorrevole"] = " Happy hour dalle 18 alle 20 ", ["TitoloConto"] = "Il tuo conto", ["Grazie"] = "Grazie" },
            ["Contenuti"] = new JsonObject
            {
                ["Media"] = new JsonArray(new JsonObject { ["Id"] = uploaded, ["Nome"] = "Aperitivo", ["HaScritte"] = true, ["Focus"] = new JsonObject { ["X"] = 0.2, ["Y"] = 0.4 } }),
                ["Promozioni"] = new JsonArray(new JsonObject { ["Nome"] = "Registrati", ["Titolo"] = "Diventa cliente", ["Qr"] = " https://example.com/registrati ", ["Modello"] = "d", ["Etichetta"] = "cliente" }),
                ["Regole"] = new JsonArray(new JsonObject { ["Id"] = "regola1", ["Nome"] = "Sempre al banco", ["Casse"] = new JsonArray("cassa1") }),
                ["Vetrine"] = new JsonArray(new JsonObject
                {
                    ["Nome"] = "Happy hour", ["Posti"] = new JsonArray("attesa"), ["Regola"] = "regola1",
                    ["Elementi"] = new JsonArray(new JsonObject { ["Id"] = uploaded }, new JsonObject { ["Id"] = "promo1" }),
                }),
            },
        };
        var saved = await agent.Admin.PutAsJsonAsync("/api/bo/marchi/locale", brand);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("in-onda", (string)(await Json(saved))["stati"]!["vetrina1"]!["stato"]!);

        var config = await Receive(socket, Type("cfg"));
        Assert.Equal("notturno", (string)config["screen"]!["tema"]!);
        Assert.Equal("Bar Centrale", (string)config["screen"]!["identita"]!["nomeLocale"]!);
        Assert.Equal("Happy hour dalle 18 alle 20", (string)config["ticker"]!);
        var idle = config["showcase"]!["attesa"]!;
        Assert.Equal([uploaded, "promo1"], idle["sequenza"]!.AsArray().Select(i => (string)i!));   // l'id della promozione lo dà l'Agent
        var photo = idle["elementi"]![0]!["media"]!;
        Assert.Equal(("/media/" + uploaded, 0.2, true), ((string)photo["src"]!, (double)photo["focus"]!["x"]!, (bool)photo["scritte"]!));
        Assert.Equal(HttpStatusCode.OK, (await device.Http.GetAsync((string)photo["src"]!)).StatusCode);
        Assert.StartsWith("data:image/png;base64,", (string)idle["elementi"]![1]!["qr"]!["png"]!);

        var settings = await Config(agent);
        var stored = settings["Marchi"]![0]!;
        Assert.Equal("https://example.com/registrati", (string)stored["Contenuti"]!["Promozioni"]![0]!["Qr"]!);
        Assert.Equal("D", (string)stored["Contenuti"]!["Promozioni"]![0]!["Modello"]!);
        Assert.Equal("Aperitivo", (string)stored["Contenuti"]!["Media"]![0]!["Nome"]!);
        Assert.Equal("in-onda", (string)settings["Stati"]!["locale"]!["vetrina1"]!["stato"]!);
        Assert.Equal("cassa1", (string)settings["Stati"]!["locale"]!["vetrina1"]!["dove"]![0]!["cassa"]!);
        Assert.Equal(10, settings["Temi"]!.AsArray().Count);

        var status = await agent.Status();
        Assert.Equal("Happy hour", (string)status["casse"]![0]!["inOnda"]!["attesa"]!["nome"]!);
        Assert.Null(status["casse"]![0]!["inOnda"]!["conto"]);

        var log = JsonNode.Parse(await agent.Admin.GetStringAsync("/api/bo/log"))!;
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("il locale e le vetrine e i contenuti cambiati dal back office"));
        Assert.Contains(log["righe"]!.AsArray(), l => ((string)l!).Contains("attesa: Happy hour, accanto al conto: niente, Grazie: niente"));
    }

    [Fact]
    public async Task A_brand_that_cannot_be_read_is_refused_and_the_preview_says_why()
    {
        await using var agent = await Start();
        var look = new JsonObject
        {
            ["Tema"] = "bluetech", ["Modo"] = "fisso", ["Fondo"] = "mesh",
            ["Colori"] = new JsonObject
            {
                ["Fondo"] = "#0B1017", ["Superficie"] = "rgba(255,255,255,0.055)", ["Testo"] = "#30343A", ["Tenue"] = "#9AA6BA",
                ["Primario"] = "#1F6FEB", ["SulPrimario"] = "#FFFFFF", ["Accento"] = "#3DDC97", ["Secondario"] = "#8FA3C0",
            },
        };
        var refused = await agent.Admin.PutAsJsonAsync("/api/bo/marchi/locale", new JsonObject { ["Aspetto"] = look });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("La correzione più vicina: testo #", (string)(await Json(refused))["errore"]!);

        var draft = new JsonObject
        {
            ["Marchio"] = "locale",
            ["Aspetto"] = look.DeepClone(),
            ["Contenuti"] = new JsonObject
            {
                ["Promozioni"] = new JsonArray(new JsonObject { ["Id"] = "promo1", ["Nome"] = "Venerdì", ["Titolo"] = "Musica dal vivo", ["Modello"] = "A" }),
                ["Vetrine"] = new JsonArray(new JsonObject
                {
                    ["Id"] = "vetrina1", ["Nome"] = "Venerdì sera", ["Attiva"] = false, ["Posti"] = new JsonArray("attesa", "conto"),
                    ["Elementi"] = new JsonArray(new JsonObject { ["Id"] = "promo1" }),
                }),
            },
            ["Override"] = new JsonObject { ["Disposizione"] = "palmare" },
        };
        var preview = await Json(await agent.Admin.PostAsJsonAsync("/api/bo/anteprima?cassa=cassa1", draft));
        Assert.Equal("cfg", (string)preview["cfg"]!["t"]!);
        Assert.Equal("#30343A", (string)preview["cfg"]!["screen"]!["colori"]!["text"]!);   // la bozza com'è: lo schermo ripiegherebbe
        Assert.Equal("palmare", (string)preview["cfg"]!["screen"]!["disposizione"]!);
        Assert.Contains(preview["problemi"]!.AsArray(), p => ((string)p!).Contains("poco leggibili"));
        Assert.NotNull(preview["anteprimaQr"]);
        Assert.Empty(preview["cfg"]!["showcase"]!.AsObject());   // la vetrina è spenta
        Assert.Equal("spenta", (string)preview["stati"]!["vetrina1"]!["stato"]!);

        var trying = await Json(await agent.Admin.PostAsJsonAsync("/api/bo/anteprima?cassa=cassa1&vetrina=vetrina1", draft));
        Assert.Equal("promo1", (string)trying["cfg"]!["showcase"]!["conto"]!["sequenza"]![0]!);   // la vetrina che si sta provando, spenta o no
        Assert.Null(trying["cfg"]!["showcase"]!["grazie"]);
        var stored = (await Config(agent))["Marchi"]![0]!;
        Assert.Null(stored["Aspetto"]!["Colori"]);   // l'anteprima non salva
        Assert.Empty(stored["Contenuti"]!["Vetrine"]!.AsArray());
    }

    [Fact]
    public async Task The_customer_page_brings_its_themes_fonts_and_the_preview_for_the_back_office()
    {
        await using var agent = await Start();
        var stranger = agent.NewBrowser();
        var script = await stranger.Http.GetStringAsync("/aspetto/tema.js");
        Assert.Contains("\"brace\":{\"id\":\"brace\"", script);
        Assert.Contains("\"gemello\":\"forno\"", script);
        Assert.Contains("\"Work Sans\"", script);
        foreach (var file in new[] { "archivo-latin-wght-normal.woff2", "oswald-latin-wght-normal.woff2", "spectral-latin-600-normal.woff2", "outfit-latin-wght-normal.woff2" })
        {
            var font = await stranger.Http.GetAsync("/fonts/" + file);
            Assert.Equal(HttpStatusCode.OK, font.StatusCode);
            Assert.Equal("font/woff2", font.Content.Headers.ContentType!.MediaType);
        }
        var css = await stranger.Http.GetStringAsync("/fonts/fonts.css");
        foreach (var family in Palettes.Fonts.Keys)
            Assert.Contains($"font-family: '{family}'", css);   // le quattordici famiglie, tutte dall'Agent
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
    public async Task Removing_a_cash_point_switches_off_the_showcases_whose_rule_was_only_for_it()
    {
        await using var agent = await Start(s =>
        {
            AddCash(s, "cassa2", "Bar");
            var c = Brand(s).Contenuti;
            c.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Colazione", Titolo = "Caffè e cornetto" });
            c.Regole.Add(new RuleConfig { Id = "regola1", Nome = "Solo bar", Casse = ["cassa2"] });
            c.Regole.Add(new RuleConfig { Id = "regola2", Nome = "Tutte e due", Casse = ["cassa1", "cassa2"] });
            c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina1", Nome = "Solo bar", Posti = ["conto"], Regola = "regola1", Elementi = [new() { Id = "promo1" }] });
            c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina2", Nome = "Tutte e due", Posti = ["conto"], Regola = "regola2", Elementi = [new() { Id = "promo1" }] });
        });
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/casse/cassa2")).StatusCode);
        var content = (await Config(agent))["Marchi"]![0]!["Contenuti"]!;
        var showcases = content["Vetrine"]!.AsArray();
        Assert.False((bool)showcases[0]!["Attiva"]!);   // non passa a tutte le casse
        Assert.Empty(content["Regole"]![0]!["Casse"]!.AsArray());
        Assert.True((bool)showcases[1]!["Attiva"]!);
        Assert.Equal(["cassa1"], content["Regole"]![1]!["Casse"]!.AsArray().Select(c => (string)c!));
    }

    [Fact]
    public async Task Brands_are_added_by_the_technician_and_removed_only_when_unused()
    {
        await using var agent = await Start();
        var added = await agent.Admin.PostAsJsonAsync("/api/bo/marchi", new { nome = "Il bar", copiaDa = "locale" });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal("marchio1", (string)(await Json(added))["id"]!);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.PutAsJsonAsync("/api/bo/casse/cassa1", new { Nome = "Bar", FileCassa = agent.CashFile(), Marchio = "marchio1" })).StatusCode);
        Assert.Contains("è il marchio di Bar", (string)(await Json(await agent.Admin.DeleteAsync("/api/bo/marchi/marchio1")))["errore"]!);
        Assert.Equal(HttpStatusCode.OK, (await agent.Admin.DeleteAsync("/api/bo/marchi/locale")).StatusCode);
        Assert.Contains("almeno un marchio", (string)(await Json(await agent.Admin.DeleteAsync("/api/bo/marchi/marchio1")))["errore"]!);
    }

    [Fact]
    public void Missing_files_are_dropped_from_the_lists_and_stray_ones_from_the_disk()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bt-media-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(folder);
            var kept = new string('c', 20) + ".png";
            var gone = new string('d', 20) + ".jpg";
            Assert.Null(store.Update(s =>
            {
                s.Casse[0].FileCassa = Path.Combine(folder, "cassa1", "Scontrino.txt");
                var c = s.Marchi[0].Contenuti;
                c.Media.Add(new MediaItem { Id = kept, Nome = "logo.png", Tipo = "image/png" });
                c.Media.Add(new MediaItem { Id = gone, Nome = "spritz.jpg", Tipo = "image/jpeg" });
                s.Marchi[0].Identita.Logo = kept;
                s.Installazione.Media.Add(new MediaItem { Id = gone, Nome = "spritz.jpg", Tipo = "image/jpeg" });
                s.Installazione.Loghi.BluetechChiaro = gone;
                c.Promozioni.Add(new PromoConfig { Id = "promo1", Nome = "Spritz", Immagine = gone, Modello = "B" });
                c.Promozioni.Add(new PromoConfig { Id = "promo2", Nome = "Pizza", Titolo = "La pizza del mese", Immagine = gone, Modello = "C" });
                c.Vetrine.Add(new ShowcaseConfig { Id = "vetrina1", Nome = "Sempre", Posti = ["attesa"], Elementi = [new() { Id = gone }, new() { Id = "promo1" }, new() { Id = "promo2" }] });
                return null;
            }));
            var media = Path.Combine(folder, "media");
            Directory.CreateDirectory(media);
            File.WriteAllBytes(Path.Combine(media, kept), [1]);
            File.WriteAllBytes(Path.Combine(media, new string('e', 20) + ".png"), [1]);   // non è in nessun elenco
            File.WriteAllBytes(Path.Combine(media, ".caricamento-123.tmp"), [1]);
            File.WriteAllBytes(Path.Combine(media, "leggimi.txt"), [1]);   // non è dell'Agent: resta
            var runtime = new Server.AgentRuntime { DataFolder = folder, LogFolder = folder, DisplayFolder = folder, StartupNetwork = new NetworkSettings() };
            var notes = new MediaLibrary(store, runtime, Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaLibrary>.Instance).Tidy();

            Assert.Contains(notes, n => n.Contains("spritz.jpg"));
            Assert.Contains(notes, n => n.Contains("la promozione Spritz è tolta"));
            var brand = store.Current.Marchi[0];
            Assert.Equal([kept], brand.Contenuti.Media.Select(m => m.Id));
            Assert.Empty(store.Current.Installazione.Media);
            Assert.Equal(kept, brand.Identita.Logo);
            Assert.Null(store.Current.Installazione.Loghi.BluetechChiaro);   // la firma chiara torna al marchio di serie
            var pizza = Assert.Single(brand.Contenuti.Promozioni);   // la pizza ha ancora il titolo
            Assert.Null(pizza.Immagine);
            Assert.Equal("A", pizza.Modello);   // senza foto il modello C non regge
            Assert.Equal(["promo2"], brand.Contenuti.Vetrine[0].Elementi.Select(e => e.Id));
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
