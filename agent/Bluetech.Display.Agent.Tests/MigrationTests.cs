using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Accesso;
using Bluetech.Display.Agent.Impostazioni;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// impostazioni.json dalla versione 2 (e dalla prima) alla 3, all'avvio e da sola (SPEC §19): niente da rifare a mano, il
/// file di prima in impostazioni.v2.bak, il log che dice riga per riga cosa è stato spostato.
/// </summary>
public sealed class MigrationTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "bt-migra-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_folder, SettingsStore.FileName);

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

    static readonly string Logo = new string('a', 20) + ".png";
    static readonly string Pasta = new string('b', 20) + ".jpg";
    static readonly string Bluetech = new string('c', 20) + ".png";
    static readonly string Satispay = new string('d', 20) + ".png";

    JsonObject Version2()
    {
        var password = AdminAccess.Hash("password-del-locale");
        JsonObject Media(string id, string name, string type) => new() { ["Id"] = id, ["Nome"] = name, ["Tipo"] = type, ["Byte"] = 10 };
        JsonObject Set(string id, string name, string place, bool rule) => new()
        {
            ["Id"] = id, ["Nome"] = name, ["Attivo"] = true, ["Dove"] = new JsonArray(place), ["Ordine"] = "casuale", ["QrDopo"] = 2, ["Secondi"] = 8,
            ["Elementi"] = new JsonArray(new JsonObject { ["Id"] = "promo1" }, new JsonObject { ["Id"] = Pasta, ["Secondi"] = 10 }),
            ["Casse"] = rule ? new JsonArray("cassa2", "cassa1") : new JsonArray(),
            ["Giorni"] = rule ? new JsonArray(5, 6) : new JsonArray(),
            ["Dalle"] = rule ? "18:00" : null, ["Alle"] = rule ? "20:30" : null,
        };
        return new JsonObject
        {
            ["Password"] = new JsonObject { ["Iterazioni"] = password.Iterazioni, ["Sale"] = password.Sale, ["Hash"] = password.Hash },
            ["Rete"] = new JsonObject { ["Porta"] = 5090, ["ApertoInRete"] = true },
            ["BackOfficeDallaRete"] = true,
            ["ChiusuraVenditaDopoSecondi"] = 75,
            ["ScontrinoDigitale"] = new JsonObject { ["MostraScontrino"] = true, ["CopiaCartacea"] = true, ["SecondiAVideo"] = 40, ["Dicitura"] = "Copia di cortesia." },
            ["Casse"] = new JsonArray(
                new JsonObject { ["Id"] = "cassa1", ["Nome"] = "Sala", ["FileCassa"] = Path.Combine(_folder, "sala", "Scontrino.txt") },
                new JsonObject { ["Id"] = "cassa2", ["Nome"] = "Bar", ["FileCassa"] = Path.Combine(_folder, "bar", "Scontrino.txt") }),
            ["Stampanti"] = new JsonArray(new JsonObject { ["Id"] = "stampante1", ["Nome"] = "Banco", ["Collegamento"] = "rete", ["Indirizzo"] = "192.168.1.50" }),
            ["Dispositivi"] = new JsonArray(
                new JsonObject { ["Id"] = "dispositivo1", ["Nome"] = "iPad Pro", ["Token"] = new string('e', 64), ["Cassa"] = "cassa1", ["Disposizione"] = "larga", ["Stampante"] = "stampante1" },
                new JsonObject { ["Id"] = "dispositivo2", ["Nome"] = "Monitor bar", ["Token"] = new string('f', 64), ["Cassa"] = "cassa2" }),
            ["Aspetto"] = new JsonObject
            {
                ["NomeLocale"] = "Trattoria del Porto", ["Sottotitolo"] = "dal 1987", ["Logo"] = Logo,
                ["LogoBluetech"] = Bluetech, ["LogoSatispay"] = Satispay,
                ["Tema"] = "trattoria", ["Modo"] = "auto", ["GiornoDalle"] = "08:00", ["GiornoAlle"] = "18:30",
                ["Footer"] = "scuro", ["Fondo"] = "luci", ["Animazioni"] = "ridotte", ["QrPagamento"] = "schermo",
                ["PrezziSoloSulConto"] = true, ["Benvenuto"] = "Benvenuti al Porto", ["Scorrevole"] = "Pesce fresco ogni giorno",
                ["TitoloConto"] = "Il conto", ["Grazie"] = "A presto!", ["SecondiGrazie"] = 25,
                ["Promozioni"] = new JsonArray(
                    new JsonObject { ["Id"] = "promo1", ["Nome"] = "Pasta", ["Titolo"] = "La pasta del giorno", ["Immagine"] = Pasta },
                    new JsonObject { ["Id"] = "promo2", ["Nome"] = "Recensioni", ["Titolo"] = "Ti è piaciuto?", ["Qr"] = "https://example.com/recensioni", ["QrDidascalia"] = "Lascia una recensione" }),
                ["Set"] = new JsonArray(
                    Set("set1", "Aperitivo", "attesa", rule: true),
                    Set("set2", "Serata jazz", "conto", rule: true),
                    Set("set3", "Sempre", "grazie", rule: false)),
                ["Media"] = new JsonArray(Media(Logo, "logo.png", "image/png"), Media(Pasta, "pasta.jpg", "image/jpeg"),
                    Media(Bluetech, "bluetech.png", "image/png"), Media(Satispay, "satispay.png", "image/png")),
            },
        };
    }

    [Fact]
    public void A_version_2_file_becomes_version_3_with_a_backup_and_the_moves_in_the_log()
    {
        Directory.CreateDirectory(_folder);
        var original = Version2().ToJsonString();
        File.WriteAllText(FilePath, original);
        var store = new SettingsStore(_folder);
        var s = store.Current;

        Assert.Equal(3, s.Versione);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_folder, SettingsStore.V2BackupName)));   // il file di prima, intero
        Assert.Contains("\"Versione\": 3", File.ReadAllText(FilePath));
        Assert.Null(s.Validate());

        // L'installazione: rete, accesso dalla rete, la password del titolare, i loghi dei kit.
        Assert.Equal((5090, true, true), (s.Installazione.Rete.Porta, s.Installazione.Rete.ApertoInRete, s.Installazione.BackOfficeDallaRete));
        var access = Assert.Single(s.Installazione.Accessi);
        Assert.Equal(AccessEntry.Owner, access.Ruolo);
        Assert.True(AdminAccess.Verify(access.Password, "password-del-locale"));
        Assert.Equal((Bluetech, Satispay), (s.Installazione.Loghi.Bluetech, s.Installazione.Loghi.Satispay));
        Assert.Equal([Bluetech, Satispay], s.Installazione.Media.Select(m => m.Id));

        // L'aspetto è il primo marchio, con il nome del locale.
        var brand = Assert.Single(s.Marchi);
        Assert.Equal(("locale", "Trattoria del Porto"), (brand.Id, brand.Nome));
        Assert.Equal(("Trattoria del Porto", "dal 1987", Logo), (brand.Identita.NomeLocale, brand.Identita.Sottotitolo, brand.Identita.Logo));
        Assert.Equal(("brace", "auto", "08:00", "18:30"), (brand.Aspetto.Tema, brand.Aspetto.Modo, brand.Aspetto.GiornoDalle, brand.Aspetto.GiornoAlle));   // trattoria di notte, forno di giorno
        Assert.Null(brand.Aspetto.Footer);   // con giorno e notte la firma la sceglie il tema
        Assert.Equal("mesh", brand.Aspetto.Fondo);
        Assert.Equal(("eco", "fullscreen", true), (brand.Schermo.Animazioni, brand.Schermo.QrPagamento, brand.Schermo.PrezziSoloSulConto));
        Assert.Equal(("vivo", 16, 10, "basso"), (brand.Schermo.Attrazione, brand.Schermo.ColonneConto, brand.Schermo.ColonneContoPagamento, brand.Schermo.ColonnaAllineamento));   // i valori di oggi
        Assert.Equal(("Benvenuti al Porto", "Pesce fresco ogni giorno", "Il conto", "A presto!", 25),
            (brand.Scritte.Benvenuto, brand.Scritte.Scorrevole, brand.Scritte.TitoloConto, brand.Scritte.Grazie, brand.Scritte.SecondiGrazie));
        var receipt = brand.Vendita.ScontrinoDigitale;
        Assert.Equal((75, true, true, 40, "Copia di cortesia."), (brand.Vendita.ChiusuraDopoSecondi, receipt.Attivo, receipt.CopiaCarta, receipt.Secondi, receipt.Dicitura));
        Assert.Equal(["carta", "qr"], receipt.Blocchi.Where(b => b.Attivo).Select(b => b.Tipo));

        // I file del locale restano del marchio; quelli dei kit no.
        Assert.Equal([Logo, Pasta], brand.Contenuti.Media.Select(m => m.Id));
        var promos = brand.Contenuti.Promozioni;
        Assert.Equal(("C", "nessuna"), (promos[0].Modello, promos[0].Etichetta));   // la foto con il titolo del sistema
        Assert.Equal("D", promos[1].Modello);

        // Casse, giorni e orari di ogni set sono una regola; due uguali sono una, con il nome di entrambi.
        var rule = Assert.Single(brand.Contenuti.Regole);
        Assert.Equal(("Aperitivo · Serata jazz", "18:00", "20:30"), (rule.Nome, rule.Dalle, rule.Alle));
        Assert.Equal(["cassa1", "cassa2"], rule.Casse);
        Assert.Equal([5, 6], rule.Giorni);
        var showcases = brand.Contenuti.Vetrine;
        Assert.Equal(["Aperitivo", "Serata jazz", "Sempre"], showcases.Select(v => v.Nome));
        Assert.Equal(["set1", "set2", "set3"], showcases.Select(v => v.Id));   // gli stessi id: le statistiche e i link restano
        Assert.Equal([rule.Id, rule.Id, null], showcases.Select(v => v.Regola));
        Assert.Equal(("casuale", 2, 8), (showcases[0].Rotazione, showcases[0].QrOgni, showcases[0].Secondi));
        Assert.Equal(new (string, int?)[] { ("promo1", null), (Pasta, 10) }, showcases[0].Elementi.Select(e => (e.Id, e.Secondi)));

        // Casse e schermi: abbinati come prima, la disposizione è un'eccezione dello schermo.
        Assert.All(s.Casse, c => Assert.Equal("locale", c.Marchio));
        Assert.Equal(["dispositivo1", "dispositivo2"], s.Schermi.Select(d => d.Id));
        Assert.Equal(("wide", "stampante1", new string('e', 64)), (s.Schermi[0].Override!.Disposizione, s.Schermi[0].Stampante, s.Schermi[0].Token));
        Assert.Null(s.Schermi[1].Override);

        Assert.Contains(store.LoadNotes, n => n.Contains("era della versione 2: portato alla 3") && n.Contains(SettingsStore.V2BackupName));
        Assert.Contains(store.LoadNotes, n => n.Contains("migrazione: il tema trattoria (giorno e notte automatici) è Brace, con Forno di giorno"));
        Assert.Contains(store.LoadNotes, n => n.Contains("migrazione: la regola di Serata jazz è uguale a quella di Aperitivo"));
        Assert.Contains(store.LoadNotes, n => n.Contains("migrazione: lo schermo iPad Pro tiene la disposizione larga"));
        Assert.Contains(store.LoadNotes, n => n.Contains("la password del back office è quella del titolare"));

        // Al prossimo avvio non si rifà niente, e il .bak resta quello della 2.
        var again = new SettingsStore(_folder);
        Assert.DoesNotContain(again.LoadNotes, n => n.Contains("migrazione"));
        Assert.Equal(original, File.ReadAllText(Path.Combine(_folder, SettingsStore.V2BackupName)));
    }

    [Fact]
    public void A_theme_without_a_day_version_stays_fixed_and_the_log_says_so()
    {
        Directory.CreateDirectory(_folder);
        var old = Version2();
        old["Aspetto"]!["Tema"] = "cocktail";
        File.WriteAllText(FilePath, old.ToJsonString());
        var store = new SettingsStore(_folder);
        Assert.Equal(("notturno", "fisso"), (store.Current.Marchi[0].Aspetto.Tema, store.Current.Marchi[0].Aspetto.Modo));
        Assert.Contains(store.LoadNotes, n => n.Contains("Notturno, che non ha una versione di giorno: resta fisso"));

        // Giorno o notte fissi: il tema di quel momento, con la firma che aveva.
        old["Aspetto"]!["Tema"] = "fine";
        old["Aspetto"]!["Modo"] = "giorno";
        old["Aspetto"]!["Footer"] = "chiaro";
        File.WriteAllText(FilePath, old.ToJsonString());
        File.Delete(Path.Combine(_folder, SettingsStore.V2BackupName));
        var look = new SettingsStore(_folder).Current.Marchi[0].Aspetto;
        Assert.Equal(("sale", "fisso", "dark"), (look.Tema, look.Modo, look.Footer));   // la firma per i fondi chiari è quella scura
    }

    [Fact]
    public void The_promotions_of_the_first_version_reach_version_3_too()
    {
        Directory.CreateDirectory(_folder);
        var photo = new string('a', 20) + ".jpg";
        var video = new string('b', 20) + ".mp4";
        var old = new JsonObject
        {
            ["Casse"] = new JsonArray(new JsonObject { ["Id"] = "cassa1", ["Nome"] = "Cassa 1", ["FileCassa"] = Path.Combine(_folder, "cassa1", "Scontrino.txt") }),
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
        File.WriteAllText(FilePath, old.ToJsonString());

        var store = new SettingsStore(_folder);
        var c = store.Current.Marchi[0].Contenuti;
        Assert.Equal(["promo1", "promo3", "promo4"], c.Promozioni.Select(p => p.Id));   // il forno era solo un video: ora è nella vetrina
        Assert.Equal(photo, c.Promozioni[0].Immagine);
        Assert.Equal(new DateOnly(2026, 12, 31), c.Promozioni[0].AlGiorno);
        var qr = c.Promozioni[2];
        Assert.Equal(("QR dopo il pagamento", "Scopri le promozioni!", "https://example.com/promozioni", "Inquadra il QR", "D"), (qr.Nome, qr.Titolo, qr.Qr, qr.QrDidascalia, qr.Modello));
        Assert.Equal(45, store.Current.Marchi[0].Scritte.SecondiGrazie);
        Assert.Equal(["Attesa", "Accanto al conto", "Grazie"], c.Vetrine.Select(x => x.Nome));
        Assert.Equal(new (string, int?)[] { (photo, 9), (video, null) }, c.Vetrine[0].Elementi.Select(e => (e.Id, e.Secondi)));
        Assert.Equal(["promo1"], c.Vetrine[1].Elementi.Select(e => e.Id));   // la promozione spenta resta nella galleria, fuori dalle vetrine
        Assert.Equal(["promo4"], c.Vetrine[2].Elementi.Select(e => e.Id));
        Assert.Empty(c.Regole);
        Assert.Null(store.Current.Validate());
        // Il venerdì dello spritz va rimesso sulla vetrina: lo dice il log.
        Assert.Contains(store.LoadNotes, n => n.Contains("nei set Attesa, Accanto al conto, Grazie") && n.Contains("controllali dal back office"));

        var saved = File.ReadAllText(FilePath);
        Assert.DoesNotContain("NelleSlide", saved);
        Assert.DoesNotContain("QrGrazie", saved);
        var again = new SettingsStore(_folder);
        Assert.Equal(3, again.Current.Marchi[0].Contenuti.Vetrine.Count);
        Assert.DoesNotContain(again.LoadNotes, n => n.Contains("prima versione"));
    }
}
