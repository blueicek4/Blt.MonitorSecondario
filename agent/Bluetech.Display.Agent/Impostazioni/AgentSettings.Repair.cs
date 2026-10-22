using System.Globalization;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Impostazioni;

public sealed partial class AgentSettings
{
    /// <summary>
    /// Sistema un file scritto a mano: liste mancanti, id mancanti o doppi, riferimenti a casse, stampanti, file, regole
    /// che non ci sono più. Restituisce cosa ha cambiato, per il log.
    /// </summary>
    public List<string> Repair()
    {
        var notes = new List<string>();
        Versione = CurrentVersion;
        Installazione ??= new InstallationSettings();
        var install = Installazione;
        install.Rete ??= new NetworkSettings();
        install.Rete.Https ??= new HttpsSettings();
        install.Accessi = (install.Accessi ?? []).Where(a => a?.Password != null).ToList();
        install.Loghi ??= new KitLogos();
        install.Media = (install.Media ?? []).Where(m => m != null).ToList();
        Marchi = (Marchi ?? []).Where(m => m != null).ToList();
        Casse = (Casse ?? []).Where(c => c != null).ToList();
        Stampanti = (Stampanti ?? []).Where(p => p != null).ToList();
        Schermi = (Schermi ?? []).Where(d => d != null).ToList();

        if (Marchi.Count == 0)
        {
            Marchi.Add(new BrandConfig { Id = "locale", Nome = "Il locale" });
            notes.Add("nessun marchio: creato \"Il locale\"");
        }
        FixIds(Marchi, "marchio", m => m.Id, (m, id) => m.Id = id, notes);
        FixIds(Casse, "cassa", c => c.Id, (c, id) => c.Id = id, notes);
        FixIds(Stampanti, "stampante", p => p.Id, (p, id) => p.Id = id, notes);
        FixIds(Schermi, "schermo", d => d.Id, (d, id) => d.Id = id, notes);

        foreach (var cash in Casse)
        {
            if (Brand(cash.Marchio) == null)
            {
                notes.Add($"{cash.Nome}: il marchio \"{cash.Marchio}\" non c'è, passa a {Marchi[0].Nome}");
                cash.Marchio = Marchi[0].Id;
            }
            if (cash.Override is { } o && (o.Scritte == null || (o.Scritte.Benvenuto ?? o.Scritte.Scorrevole ?? o.Scritte.TitoloConto ?? o.Scritte.Grazie) == null))
                cash.Override = null;
        }
        foreach (var screen in Schermi)
        {
            if (screen.Stampante != null && Printer(screen.Stampante) == null)
            {
                notes.Add($"{screen.Nome}: la stampante {screen.Stampante} non c'è più");
                screen.Stampante = null;
            }
            if (CashPoint(screen.Cassa) == null && Casse.Count > 0)
            {
                notes.Add($"{screen.Nome}: la cassa {screen.Cassa} non c'è più, passa a {Casse[0].Nome}");
                screen.Cassa = Casse[0].Id;
            }
            if (screen.Override is { IsEmpty: true })
                screen.Override = null;
        }
        foreach (var (name, get, set) in KitLogos.All)
        {
            if (get(install.Loghi) is { } logo && install.Media.Find(m => m.Id == logo) == null)
            {
                notes.Add($"{name} {logo} non c'è più");
                set(install.Loghi, null);
            }
        }
        foreach (var brand in Marchi)
            RepairBrand(brand, notes);
        return notes;
    }

    void RepairBrand(BrandConfig brand, List<string> notes)
    {
        brand.Nome ??= "";
        brand.Identita ??= new BrandIdentity();
        brand.Aspetto ??= new BrandLook();
        brand.Schermo ??= new BrandScreen();
        brand.Scritte ??= new BrandTexts();
        brand.Vendita ??= new BrandSale();
        brand.Vendita.ScontrinoDigitale ??= new DigitalReceiptConfig();
        brand.Contenuti ??= new BrandContent();
        var c = brand.Contenuti;
        c.Media = (c.Media ?? []).Where(m => m != null).ToList();
        c.Promozioni = (c.Promozioni ?? []).Where(p => p != null).ToList();
        c.Regole = (c.Regole ?? []).Where(r => r != null).ToList();
        c.Vetrine = (c.Vetrine ?? []).Where(v => v != null).ToList();
        FixIds(c.Promozioni, "promo", p => p.Id, (p, id) => p.Id = id, notes);
        FixIds(c.Regole, "regola", r => r.Id, (r, id) => r.Id = id, notes);
        FixIds(c.Vetrine, "vetrina", v => v.Id, (v, id) => v.Id = id, notes);
        var where = Marchi.Count > 1 ? brand.Nome + ": " : "";

        var receipt = brand.Vendita.ScontrinoDigitale;
        receipt.Blocchi = (receipt.Blocchi ?? []).Where(b => b != null && ReceiptBlock.Types.Contains(b.Tipo)).GroupBy(b => b.Tipo).Select(g => g.First()).ToList();
        foreach (var missing in ReceiptBlock.Defaults().Where(d => receipt.Blocchi.All(b => b.Tipo != d.Tipo)))
            receipt.Blocchi.Add(new ReceiptBlock { Tipo = missing.Tipo, Attivo = false, Colonna = missing.Colonna, Riga = missing.Riga });

        foreach (var media in c.Media)
        {
            if (media.Poster != null && brand.Media(media.Poster) == null)
                media.Poster = null;
            if (media.PosterSfocato != null && brand.Media(media.PosterSfocato) == null)
                media.PosterSfocato = null;
        }
        foreach (var (label, get, set) in IdentityImages)
        {
            if (get(brand) is { } id && brand.Media(id) == null)
            {
                notes.Add($"{where}{label} {id} non c'è più");
                set(brand, null);
            }
        }
        if (brand.Aspetto.Fondo is "immagine" or "video" && brand.Aspetto.FondoMedia == null)
            brand.Aspetto.Fondo = "mesh";
        foreach (var promo in c.Promozioni)
        {
            promo.Nome ??= "";
            promo.Titolo ??= "";
            promo.Testo ??= "";
            promo.QrDidascalia ??= "";
            promo.Etichetta ??= "promozione";
            promo.Modello ??= PromoConfig.Suggested(promo.HasQr, brand.Media(promo.Immagine));
            if (promo.Immagine != null && brand.Media(promo.Immagine) == null)
            {
                notes.Add($"{where}{promo.Nome}: l'immagine {promo.Immagine} non c'è più");
                promo.Immagine = null;
            }
        }
        var orphanRules = new HashSet<string>();
        foreach (var rule in c.Regole)
        {
            rule.Nome ??= "";
            rule.Casse = (rule.Casse ?? []).Distinct().ToList();
            rule.Giorni = (rule.Giorni ?? []).Distinct().Order().ToList();
            var stale = rule.Casse.Where(id => CashPoint(id) == null).ToList();
            foreach (var missing in stale)
            {
                notes.Add($"{where}la regola {rule.Nome}: la cassa {missing} non c'è più");
                rule.Casse.Remove(missing);
            }
            if (stale.Count > 0 && rule.Casse.Count == 0)
                orphanRules.Add(rule.Id);   // era solo per casse che non ci sono più: le sue vetrine non devono passare a tutte
        }
        foreach (var showcase in c.Vetrine)
        {
            showcase.Nome ??= "";
            showcase.Rotazione ??= "elenco";
            showcase.Posti = (showcase.Posti ?? []).Distinct().ToList();
            showcase.Elementi = (showcase.Elementi ?? []).Where(e => e != null).ToList();
            if (showcase.Regola != null && brand.Rule(showcase.Regola) == null)
            {
                notes.Add($"{where}{showcase.Nome}: la regola {showcase.Regola} non c'è più, la vetrina è spenta");
                showcase.Regola = null;
                showcase.Attiva = false;
            }
            else if (showcase.Regola != null && orphanRules.Contains(showcase.Regola) && showcase.Attiva)
            {
                notes.Add($"{where}{showcase.Nome}: spenta, le casse della sua regola non ci sono più");
                showcase.Attiva = false;
            }
            foreach (var gone in showcase.Elementi.Where(e => brand.Media(e.Id) == null && brand.Promo(e.Id) == null).ToList())
            {
                notes.Add($"{where}{showcase.Nome}: {gone.Id} non c'è più");
                showcase.Elementi.Remove(gone);
            }
        }
    }

    /// <summary>Le immagini dell'identità e del fondo di un marchio, con il nome che hanno nei messaggi.</summary>
    internal static readonly (string Name, Func<BrandConfig, string?> Get, Action<BrandConfig, string?> Set)[] IdentityImages =
    [
        ("il logo", b => b.Identita.Logo, (b, id) => b.Identita.Logo = id),
        ("il logo per i fondi chiari", b => b.Identita.LogoChiaro, (b, id) => b.Identita.LogoChiaro = id),
        ("lo sfondo", b => b.Aspetto.FondoMedia, (b, id) => b.Aspetto.FondoMedia = id),
    ];

    /// <summary>"07:30" → 7:30; null se non è un'ora.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    [GeneratedRegex(@"^[0-9a-f]{20}\.(png|jpg|gif|webp|mp4|webm)$")]
    internal static partial Regex MediaIdPattern();
}
