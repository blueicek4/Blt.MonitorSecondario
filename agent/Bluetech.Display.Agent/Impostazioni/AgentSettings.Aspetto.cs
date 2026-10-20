using System.Globalization;
using System.Text.RegularExpressions;
using Bluetech.Display.Agent.Aspetto;

namespace Bluetech.Display.Agent.Impostazioni;

public sealed partial class AgentSettings
{
    /// <summary>Come appare lo schermo del cliente: logo, tema, scritte, QR del Grazie, promozioni.</summary>
    public AppearanceSettings Aspetto { get; set; } = new();

    public MediaItem? Media(string? id) => id == null ? null : Aspetto.Media.Find(m => m.Id == id);

    void RepairAppearance(List<string> notes)
    {
        Aspetto ??= new AppearanceSettings();
        var a = Aspetto;
        a.QrGrazie ??= new ThanksQrSettings();
        a.Promozioni ??= [];
        a.Media ??= [];
        a.Promozioni.RemoveAll(p => p == null);
        a.Media.RemoveAll(m => m == null);
        var seen = new HashSet<string>();
        foreach (var promo in a.Promozioni)
        {
            if (string.IsNullOrWhiteSpace(promo.Id) || !IdPattern().IsMatch(promo.Id) || !seen.Add(promo.Id))
            {
                var fresh = NextId("promo", a.Promozioni.Select(p => p.Id).Where(i => i != null).Concat(seen));
                notes.Add($"id \"{promo.Id}\" sostituito con {fresh}");
                promo.Id = fresh;
                seen.Add(fresh);
            }
            promo.Casse ??= [];
            promo.Giorni ??= [];
            foreach (var missing in promo.Casse.Where(c => CashPoint(c) == null).ToList())
            {
                notes.Add($"{promo.Nome}: la cassa {missing} non c'è più");
                promo.Casse.Remove(missing);
            }
            if (promo.Media != null && Media(promo.Media) == null)
            {
                notes.Add($"{promo.Nome}: l'immagine {promo.Media} non c'è più");
                promo.Media = null;
            }
        }
        if (a.Logo != null && Media(a.Logo) == null)
        {
            notes.Add($"il logo {a.Logo} non c'è più");
            a.Logo = null;
        }
        if (a.LogoChiaro != null && Media(a.LogoChiaro) == null)
        {
            notes.Add($"il logo per i fondi chiari {a.LogoChiaro} non c'è più");
            a.LogoChiaro = null;
        }
        if (a.FondoMedia != null && Media(a.FondoMedia) == null)
        {
            notes.Add($"lo sfondo {a.FondoMedia} non c'è più");
            a.FondoMedia = null;
        }
    }

    /// <summary>I colori del locale, se ci sono e se valgono (con l'automatico valgono quelli del tema, come nel design).</summary>
    public LocaleColors? LocaleColors() =>
        Aspetto.Colori is { } c && Aspetto.Modo != "auto"
            ? new LocaleColors(c.Fondo, c.Superficie, c.Testo, c.Tenue, c.Primario, c.SulPrimario, c.Accento, c.Secondario)
            : null;

    internal IEnumerable<string> AppearanceProblems()
    {
        var a = Aspetto;
        if (!Palettes.IsTheme(a.Tema))
            yield return "Aspetto: il tema è bluetech, trattoria, fine o cocktail.";
        if (a.Modo is not ("notte" or "giorno" or "auto"))
            yield return "Aspetto: giorno e notte è \"giorno\", \"notte\" o \"auto\".";
        if (a.Modo == "auto" && !(ParseTime(a.GiornoDalle) is { } dayStart && ParseTime(a.GiornoAlle) is { } dayEnd && dayStart < dayEnd))
            yield return "Aspetto: il giorno va dalle 07:00 alle 19:00, per esempio (la prima ora prima della seconda).";

        if (LocaleColors() is { } colors && Palettes.IsTheme(a.Tema) && a.Modo is "notte" or "giorno")
        {
            var named = new (string Name, string Value)[]
            {
                ("il fondo", colors.Bg), ("il testo", colors.Text), ("il testo tenue", colors.Muted), ("il primario", colors.Primary),
                ("il testo sopra il primario", colors.OnPrimary), ("l'accento", colors.Accent), ("il secondario", colors.Secondary),
            };
            var wrong = named.Where(n => ThemeColors.ParseHex(n.Value) == null).Select(n => n.Name).ToList();
            if (ThemeColors.ParseColor(colors.Surface) == null)
                wrong.Add("la card");
            if (wrong.Count > 0)
            {
                yield return $"Aspetto: {string.Join(", ", wrong)} non è un colore valido (come #1F6FEB).";
            }
            else
            {
                var basePalette = Palettes.Get(a.Tema, a.Modo);
                var failing = ThemeColors.Pairs(ThemeColors.WithLocaleColors(basePalette, colors)).Where(p => !p.Ok).ToList();
                if (failing.Count > 0)
                    yield return ContrastProblem(basePalette, colors, failing);
            }
        }

        if (a.FontTitoli != null && !Palettes.Fonts.ContainsKey(a.FontTitoli))
            yield return $"Aspetto: il font dei titoli \"{a.FontTitoli}\" non c'è. Si sceglie tra {string.Join(", ", Palettes.Fonts.Keys)}.";
        if (a.FontTesto != null && !Palettes.Fonts.ContainsKey(a.FontTesto))
            yield return $"Aspetto: il font del testo \"{a.FontTesto}\" non c'è. Si sceglie tra {string.Join(", ", Palettes.Fonts.Keys)}.";
        if (a.Raggio is < 0 or > 32)
            yield return "Aspetto: gli angoli arrotondati vanno da 0 a 32 px.";
        if (a.Vetro is < 0 or > 100)
            yield return "Aspetto: il vetro va da 0 a 100.";
        if (a.Footer is not (null or "chiaro" or "scuro"))
            yield return "Aspetto: la firma Bluetech in basso è \"chiaro\" o \"scuro\".";
        if (a.Fondo is not ("tinta" or "sfumato" or "luci" or "immagine" or "video"))
            yield return "Aspetto: lo sfondo è tinta, sfumato, luci, immagine o video.";
        else if (a.Fondo is "immagine" or "video" && !(Media(a.FondoMedia) is { } background && background.IsVideo == (a.Fondo == "video")))
            yield return a.Fondo == "video" ? "Aspetto: per lo sfondo video scegli un video." : "Aspetto: per lo sfondo a immagine scegli un'immagine.";
        if (a.Animazioni is not ("piene" or "leggere" or "ridotte"))
            yield return "Aspetto: le animazioni sono piene, leggere o ridotte.";
        if (a.QrPagamento is not ("accanto" or "schermo"))
            yield return "Aspetto: il QR del pagamento sta \"accanto\" al conto o a tutto \"schermo\".";
        if (a.Logo != null && Media(a.Logo) is not { IsVideo: false })
            yield return "Aspetto: il logo è un'immagine (PNG con lo sfondo trasparente, per esempio).";
        if (a.LogoChiaro != null && Media(a.LogoChiaro) is not { IsVideo: false })
            yield return "Aspetto: il logo per i fondi chiari è un'immagine (PNG con lo sfondo trasparente, per esempio).";

        foreach (var (text, label, max) in new[]
                 {
                     (a.NomeLocale, "il nome del locale", 40), (a.Sottotitolo, "il sottotitolo", 60), (a.Benvenuto, "il benvenuto", 80),
                     (a.Scorrevole, "il testo scorrevole", 500), (a.TitoloConto, "il titolo del conto", 30), (a.Grazie, "il saluto del Grazie", 60),
                 })
        {
            if ((text?.Length ?? 0) > max)
                yield return $"Aspetto: {label} è troppo lungo (al massimo {max} caratteri).";
        }

        var thanks = a.QrGrazie;
        if (thanks.Attivo && !(Uri.TryCreate(thanks.Indirizzo?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "https" or "http"))
            yield return "Aspetto, Grazie: l'indirizzo del QR è come https://… (recensioni, fidelity, menu digitale).";
        if ((thanks.Indirizzo?.Length ?? 0) > 400)
            yield return "Aspetto, Grazie: l'indirizzo del QR è troppo lungo (al massimo 400 caratteri).";
        if ((thanks.Titolo?.Length ?? 0) > 40 || (thanks.Didascalia?.Length ?? 0) > 120)
            yield return "Aspetto, Grazie: il titolo del QR arriva a 40 caratteri, la didascalia a 120.";
        if (thanks.Secondi is < 10 or > 120)
            yield return "Aspetto, Grazie: con il QR il Grazie resta tra 10 e 120 secondi.";

        if (a.Promozioni.Count > 50)
            yield return "Al massimo 50 promozioni.";
        foreach (var promo in a.Promozioni)
        {
            if (Name(promo.Nome, "La promozione") is { } error)
                yield return error;
            var media = Media(promo.Media);
            if (promo.Media != null && media == null)
                yield return $"{promo.Nome}: l'immagine o il video non c'è più.";
            if (!promo.NelleSlide && !promo.NelConto)
                yield return $"{promo.Nome}: scegli dove mostrarla, nelle slide dell'attesa o accanto al conto.";
            if (promo.NelleSlide && media == null)
                yield return $"{promo.Nome}: nelle slide dell'attesa serve un'immagine o un video.";
            if (promo.NelConto && string.IsNullOrWhiteSpace(promo.Titolo) && string.IsNullOrWhiteSpace(promo.Testo) && media is not { IsVideo: false })
                yield return $"{promo.Nome}: accanto al conto servono un titolo, un testo o un'immagine.";
            if (promo.SecondiImmagine is < 3 or > 60)
                yield return $"{promo.Nome}: un'immagine resta tra 3 e 60 secondi.";
            if ((promo.Titolo?.Length ?? 0) > 60 || (promo.Testo?.Length ?? 0) > 200)
                yield return $"{promo.Nome}: il titolo arriva a 60 caratteri, il testo a 200.";
            foreach (var cash in promo.Casse.Where(c => CashPoint(c) == null))
                yield return $"{promo.Nome}: la cassa {cash} non c'è.";
            if (promo.Giorni.Any(d => d is < 1 or > 7))
                yield return $"{promo.Nome}: i giorni vanno da 1 (lunedì) a 7 (domenica).";
            if ((promo.Dalle == null) != (promo.Alle == null)
                || (promo.Dalle != null && !(ParseTime(promo.Dalle) is { } from && ParseTime(promo.Alle) is { } to && from != to)))
                yield return $"{promo.Nome}: gli orari sono come 18:00 e 20:00 (anche 22:00 e 02:00, a cavallo della mezzanotte).";
            if (promo.DalGiorno > promo.AlGiorno)
                yield return $"{promo.Nome}: l'ultimo giorno viene prima del primo.";
        }

        if (a.Media.Count > 500)
            yield return "Al massimo 500 immagini e video.";
        var ids = new HashSet<string>();
        foreach (var item in a.Media)
        {
            if (!MediaIdPattern().IsMatch(item.Id ?? "") || !ids.Add(item.Id!))
                yield return $"Il file {item.Nome} ha un nome interno non valido ({item.Id}).";
        }
    }

    static string ContrastProblem(Palette basePalette, LocaleColors colors, List<ThemeColors.Pair> failing)
    {
        var what = string.Join(", ", failing.Select(p => $"{p.Label} {ThemeColors.Ratio(p.Ratio ?? 0)}"));
        var text = $"Aspetto: colori poco leggibili, {what} (serve almeno 4,5:1).";
        if (ThemeColors.Suggest(basePalette, colors) is not { } fixedColors)
            return text + " Scegli colori più distanti tra loro.";
        var changes = new List<string>();
        if (fixedColors.Text != colors.Text)
            changes.Add($"testo {fixedColors.Text}");
        if (fixedColors.OnPrimary != colors.OnPrimary)
            changes.Add($"testo sopra il primario {fixedColors.OnPrimary}");
        return text + $" La correzione più vicina: {string.Join(", ", changes)}.";
    }

    /// <summary>"07:30" → 7:30; null se non è un'ora.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    [GeneratedRegex(@"^[0-9a-f]{20}\.(png|jpg|gif|webp|mp4|webm)$")]
    internal static partial Regex MediaIdPattern();
}

public sealed class AppearanceSettings
{
    /// <summary>In alto sullo schermo, accanto al logo o al suo posto.</summary>
    public string NomeLocale { get; set; } = "";
    public string Sottotitolo { get; set; } = "";
    /// <summary>Un'immagine di <see cref="Media"/>, per i fondi scuri (e sopra le slide); null = solo il nome.</summary>
    public string? Logo { get; set; }
    /// <summary>
    /// Facoltativo: la versione del logo per i fondi chiari (i temi di giorno). Un logo chiaro su un fondo chiaro non si
    /// vede: con giorno e notte automatici serve tutti i giorni.
    /// </summary>
    public string? LogoChiaro { get; set; }

    /// <summary>bluetech, trattoria, fine o cocktail: i quattro temi del design.</summary>
    public string Tema { get; set; } = "bluetech";
    /// <summary>"notte", "giorno" o "auto" (giorno tra <see cref="GiornoDalle"/> e <see cref="GiornoAlle"/>).</summary>
    public string Modo { get; set; } = "auto";
    public string GiornoDalle { get; set; } = "07:00";
    public string GiornoAlle { get; set; } = "19:00";

    /// <summary>Solo con giorno o notte fissi: i colori del locale al posto di quelli del tema. Null = quelli del tema.</summary>
    public ThemeColorSettings? Colori { get; set; }
    /// <summary>Una delle otto famiglie del design; null = quella del tema.</summary>
    public string? FontTitoli { get; set; }
    public string? FontTesto { get; set; }
    /// <summary>Angoli, 0–32 px; null = quelli del tema.</summary>
    public int? Raggio { get; set; }
    /// <summary>Vetro della card, 0–100, solo con giorno o notte fissi; null = quello del tema.</summary>
    public int? Vetro { get; set; }
    /// <summary>"chiaro" o "scuro", solo con giorno o notte fissi; null = quella del tema.</summary>
    public string? Footer { get; set; }

    /// <summary>"tinta", "sfumato", "luci", "immagine" o "video" (con <see cref="FondoMedia"/>).</summary>
    public string Fondo { get; set; } = "luci";
    public string? FondoMedia { get; set; }
    /// <summary>"piene", "leggere" (consigliate) o "ridotte" (per i PC più lenti).</summary>
    public string Animazioni { get; set; } = "leggere";
    /// <summary>"accanto" al conto (il conto si stringe) o a tutto "schermo".</summary>
    public string QrPagamento { get; set; } = "accanto";
    /// <summary>Righe più grandi sugli schermi 4:3 (due righe in meno a video).</summary>
    public bool RigheCompatte { get; set; }
    /// <summary>
    /// Importi e totale solo quando la cassa mostra il conto (preconto, scontrino), non durante la battuta: come
    /// ShowPricesDuringOrder=false del vecchio monitor 6.0, per chi lo usa già.
    /// </summary>
    public bool PrezziSoloSulConto { get; set; }

    /// <summary>Nell'attesa quando non ci sono slide.</summary>
    public string Benvenuto { get; set; } = "Benvenuti";
    /// <summary>Il testo che scorre in basso nell'attesa; vuoto = niente.</summary>
    public string Scorrevole { get; set; } = "";
    public string TitoloConto { get; set; } = "Il tuo conto";
    public string Grazie { get; set; } = "Grazie e arrivederci";

    public ThanksQrSettings QrGrazie { get; set; } = new();

    /// <summary>In quest'ordine nelle slide dell'attesa.</summary>
    public List<PromotionConfig> Promozioni { get; set; } = [];

    /// <summary>Immagini e video caricati dal back office, in dati\media.</summary>
    public List<MediaItem> Media { get; set; } = [];
}

/// <summary>Gli otto colori dei token del design, con i nomi del back office.</summary>
public sealed class ThemeColorSettings
{
    public string Fondo { get; set; } = "";
    /// <summary>La card del conto: #rrggbb o rgba(…) per il vetro.</summary>
    public string Superficie { get; set; } = "";
    public string Testo { get; set; } = "";
    public string Tenue { get; set; } = "";
    public string Primario { get; set; } = "";
    public string SulPrimario { get; set; } = "";
    /// <summary>Sconti e maggiorazioni.</summary>
    public string Accento { get; set; } = "";
    public string Secondario { get; set; } = "";
}

/// <summary>Il QR dello stato Grazie: recensioni, fidelity, menu digitale. Il PNG lo fa l'Agent.</summary>
public sealed class ThanksQrSettings
{
    public bool Attivo { get; set; }
    public string Indirizzo { get; set; } = "";
    public string Titolo { get; set; } = "Com'è andata?";
    public string Didascalia { get; set; } = "Lasciaci una recensione: inquadra il QR";
    /// <summary>Con il QR il Grazie resta fino al conto dopo, al massimo così (senza QR 8 s).</summary>
    public int Secondi { get; set; } = 30;
}

/// <summary>Una promozione: una slide dell'attesa, una scheda accanto al conto, o tutte e due.</summary>
public sealed class PromotionConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public bool Attiva { get; set; } = true;
    public bool NelleSlide { get; set; } = true;
    public bool NelConto { get; set; }
    /// <summary>Immagine o video di <see cref="AppearanceSettings.Media"/>.</summary>
    public string? Media { get; set; }
    /// <summary>Quanto resta un'immagine nelle slide (un video dura quanto dura).</summary>
    public int SecondiImmagine { get; set; } = 7;
    /// <summary>La scheda accanto al conto.</summary>
    public string Titolo { get; set; } = "";
    public string Testo { get; set; } = "";
    /// <summary>Vuoto = tutte le casse.</summary>
    public List<string> Casse { get; set; } = [];
    /// <summary>1 = lunedì … 7 = domenica; vuoto = tutti i giorni.</summary>
    public List<int> Giorni { get; set; } = [];
    /// <summary>"18:00"; null = tutto il giorno. Alle prima di Dalle = a cavallo della mezzanotte.</summary>
    public string? Dalle { get; set; }
    public string? Alle { get; set; }
    public DateOnly? DalGiorno { get; set; }
    public DateOnly? AlGiorno { get; set; }
}

public sealed class MediaItem
{
    /// <summary>Il nome del file in dati\media: l'impronta del contenuto più l'estensione.</summary>
    public string Id { get; set; } = "";
    /// <summary>Il nome del file caricato.</summary>
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public long Byte { get; set; }
    public DateTimeOffset CaricatoIl { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsVideo => Tipo.StartsWith("video/", StringComparison.Ordinal);
}
