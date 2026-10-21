using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bluetech.Display.Agent.Aspetto;

namespace Bluetech.Display.Agent.Impostazioni;

public sealed partial class AgentSettings
{
    /// <summary>Come appare lo schermo del cliente: logo, tema, scritte, le gallerie e i set di reclame.</summary>
    public AppearanceSettings Aspetto { get; set; } = new();

    public MediaItem? Media(string? id) => id == null ? null : Aspetto.Media.Find(m => m.Id == id);
    public PromoConfig? Promo(string? id) => id == null ? null : Aspetto.Promozioni.Find(p => p.Id == id);

    void RepairAppearance(List<string> notes)
    {
        Aspetto ??= new AppearanceSettings();
        var a = Aspetto;
        a.Promozioni ??= [];
        a.Set ??= [];
        a.Media ??= [];
        a.Promozioni.RemoveAll(p => p == null);
        a.Set.RemoveAll(s => s == null);
        a.Media.RemoveAll(m => m == null);
        FixIds(a.Promozioni, "promo", p => p.Id, (p, id) => p.Id = id, notes);
        MigrateFirstVersion(notes);
        FixIds(a.Set, "set", s => s.Id, (s, id) => s.Id = id, notes);
        foreach (var promo in a.Promozioni)
        {
            promo.Vecchio = null;
            promo.Nome ??= "";
            promo.Titolo ??= "";
            promo.Testo ??= "";
            promo.QrDidascalia ??= "";
            if (promo.Immagine != null && Media(promo.Immagine) == null)
            {
                notes.Add($"{promo.Nome}: l'immagine {promo.Immagine} non c'è più");
                promo.Immagine = null;
            }
        }
        foreach (var set in a.Set)
        {
            set.Nome ??= "";
            set.Ordine ??= "elenco";
            set.Dove ??= [];
            set.Elementi ??= [];
            set.Casse ??= [];
            set.Giorni ??= [];
            set.Elementi.RemoveAll(e => e == null);
            var stale = set.Casse.Where(c => CashPoint(c) == null).ToList();
            foreach (var missing in stale)
            {
                notes.Add($"{set.Nome}: la cassa {missing} non c'è più");
                set.Casse.Remove(missing);
            }
            if (stale.Count > 0 && set.Casse.Count == 0 && set.Attivo)
            {
                set.Attivo = false;   // era solo per casse che non ci sono più: non deve passare a tutte
                notes.Add($"{set.Nome}: spento, le sue casse non ci sono più");
            }
            foreach (var gone in set.Elementi.Where(e => Media(e.Id) == null && Promo(e.Id) == null).ToList())
            {
                notes.Add($"{set.Nome}: {gone.Id} non c'è più");
                set.Elementi.Remove(gone);
            }
        }
        foreach (var (name, get, set) in AppearanceSettings.Logos)
        {
            if (get(a) is { } logo && Media(logo) == null)
            {
                notes.Add($"{name} {logo} non c'è più");
                set(a, null);
            }
        }
        if (a.FondoMedia != null && Media(a.FondoMedia) == null)
        {
            notes.Add($"lo sfondo {a.FondoMedia} non c'è più");
            a.FondoMedia = null;
        }
    }

    static readonly string[] FirstVersionKeys = ["NelleSlide", "NelConto", "SecondiImmagine", "Media"];

    /// <summary>
    /// La prima versione aveva promozioni con i loro posti (nelle slide, accanto al conto), l'immagine o il video e
    /// l'orario, e il QR del Grazie a parte. Diventano la galleria delle promozioni e i set Attesa, Accanto al conto e
    /// Grazie, nello stesso ordine. Casse, giorni e orari di ogni promozione non si portano: ora sono del set.
    /// </summary>
    void MigrateFirstVersion(List<string> notes)
    {
        var a = Aspetto;
        var thanks = a.QrGrazie;
        a.QrGrazie = null;
        var old = a.Promozioni.Where(p => p.Vecchio is { } v && v.Keys.Any(k => FirstVersionKeys.Contains(k, StringComparer.OrdinalIgnoreCase))).ToList();
        var thanksUrl = thanks?.Indirizzo?.Trim();
        if (old.Count == 0 && string.IsNullOrEmpty(thanksUrl))
            return;

        var idle = new AdSetConfig { Nome = "Attesa", Dove = ["attesa"] };
        var sale = new AdSetConfig { Nome = "Accanto al conto", Dove = ["conto"] };
        var scheduled = false;
        foreach (var promo in old)
        {
            var v = new Dictionary<string, JsonElement>(promo.Vecchio!, StringComparer.OrdinalIgnoreCase);
            bool Flag(string key, bool fallback) => v.TryGetValue(key, out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : fallback;
            var media = v.TryGetValue("Media", out var m) && m.ValueKind == JsonValueKind.String ? Media(m.GetString()) : null;
            var seconds = v.TryGetValue("SecondiImmagine", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n) ? n : 7;
            scheduled |= new[] { "Casse", "Giorni" }.Any(k => v.TryGetValue(k, out var list) && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                || new[] { "Dalle", "Alle" }.Any(k => v.TryGetValue(k, out var time) && time.ValueKind == JsonValueKind.String);
            if (media is { IsVideo: false })
                promo.Immagine = media.Id;
            var words = !string.IsNullOrWhiteSpace(promo.Titolo) || !string.IsNullOrWhiteSpace(promo.Testo);
            if (!Flag("Attiva", true))
                continue;
            if (Flag("NelleSlide", true) && media != null)
                idle.Elementi.Add(new AdItemConfig { Id = media.Id, Secondi = media.IsVideo ? null : Math.Clamp(seconds, 3, 120) });
            if (Flag("NelConto", false) && (words || promo.Immagine != null))
                sale.Elementi.Add(new AdItemConfig { Id = words ? promo.Id : promo.Immagine! });
        }
        // Senza titolo né testo una promozione era solo la sua slide: ora è la foto o il video nel set.
        a.Promozioni.RemoveAll(p => old.Contains(p) && string.IsNullOrWhiteSpace(p.Titolo) && string.IsNullOrWhiteSpace(p.Testo));

        var sets = new List<AdSetConfig> { idle, sale };
        if (!string.IsNullOrEmpty(thanksUrl))
        {
            var qr = new PromoConfig
            {
                Id = NextId("promo", a.Promozioni.Select(p => p.Id)),
                Nome = "QR dopo il pagamento",
                Titolo = thanks!.Titolo?.Trim() ?? "",
                Qr = thanksUrl,
                QrDidascalia = thanks.Didascalia?.Trim() ?? "",
            };
            a.Promozioni.Add(qr);
            sets.Add(new AdSetConfig { Nome = "Grazie", Dove = ["grazie"], Attivo = thanks.Attivo, Elementi = [new AdItemConfig { Id = qr.Id }] });
            a.SecondiGrazie = Math.Clamp(thanks.Secondi, 10, 120);
        }
        var made = new List<string>();
        foreach (var set in sets.Where(x => x.Elementi.Count > 0))
        {
            set.Id = NextId("set", a.Set.Select(x => x.Id));
            a.Set.Add(set);
            made.Add(set.Nome);
        }
        notes.Add("le promozioni della prima versione sono nella galleria"
            + (made.Count > 0 ? $" e nei set {string.Join(", ", made)}" : "")
            + (scheduled ? "; casse, giorni e orari ora si scelgono sul set: controllali dal back office" : ""));
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
        foreach (var (name, get, _) in AppearanceSettings.Logos)
        {
            if (get(a) is { } logo && Media(logo) is not { IsVideo: false })
                yield return $"Aspetto: {name} è un'immagine (PNG con lo sfondo trasparente, per esempio).";
        }

        foreach (var (text, label, max) in new[]
                 {
                     (a.NomeLocale, "il nome del locale", 40), (a.Sottotitolo, "il sottotitolo", 60), (a.Benvenuto, "il benvenuto", 80),
                     (a.Scorrevole, "il testo scorrevole", 500), (a.TitoloConto, "il titolo del conto", 30), (a.Grazie, "il saluto del Grazie", 60),
                 })
        {
            if ((text?.Length ?? 0) > max)
                yield return $"Aspetto: {label} è troppo lungo (al massimo {max} caratteri).";
        }

        if (a.SecondiGrazie is < 10 or > 120)
            yield return "Aspetto: con un QR il Grazie resta tra 10 e 120 secondi.";

        if (a.Promozioni.Count > 200)
            yield return "Al massimo 200 promozioni.";
        foreach (var promo in a.Promozioni)
        {
            if (Name(promo.Nome, "La promozione") is { } error)
                yield return error;
            var what = $"La promozione {promo.Nome}";
            var image = Media(promo.Immagine);
            if (promo.Immagine != null && image == null)
                yield return $"{what}: l'immagine non c'è più.";
            else if (image is { IsVideo: true })
                yield return $"{what}: ha un'immagine, non un video (il video si mette direttamente nel set).";
            if (!promo.HasContent)
                yield return $"{what}: scrivi un titolo o un testo, o scegli un'immagine o un QR.";
            if (promo.HasQr && !(Uri.TryCreate(promo.Qr!.Trim(), UriKind.Absolute, out var url) && url.Scheme is "https" or "http"))
                yield return $"{what}: l'indirizzo del QR è come https://… (registrazione, recensioni, menu digitale).";
            if ((promo.Qr?.Length ?? 0) > 400)
                yield return $"{what}: l'indirizzo del QR è troppo lungo (al massimo 400 caratteri).";
            if ((promo.Titolo?.Length ?? 0) > 60 || (promo.Testo?.Length ?? 0) > 200 || (promo.QrDidascalia?.Length ?? 0) > 120)
                yield return $"{what}: il titolo arriva a 60 caratteri, il testo a 200, la scritta del QR a 120.";
            if (promo.DalGiorno > promo.AlGiorno)
                yield return $"{what}: l'ultimo giorno viene prima del primo.";
        }

        if (a.Set.Count > 30)
            yield return "Al massimo 30 set.";
        foreach (var set in a.Set)
        {
            if (Name(set.Nome, "Il set") is { } error)
                yield return error;
            var what = $"Il set {set.Nome}";
            if (set.Dove.Count == 0 || set.Dove.Any(d => !AdSetConfig.Places.Contains(d)))
                yield return $"{what}: scegli dove va, nell'attesa, accanto al conto o nel Grazie.";
            if (!AdSetConfig.Orders.Contains(set.Ordine))
                yield return $"{what}: l'ordine è elenco, casuale o alfabetico.";
            if (set.QrDopo is < 0 or > 20)
                yield return $"{what}: una promozione con il QR dopo 1-20 elementi (0 = come le altre).";
            if (set.Secondi is < 3 or > 120 || set.Elementi.Any(e => e.Secondi is < 3 or > 120))
                yield return $"{what}: una foto o una promozione resta tra 3 e 120 secondi.";
            if (set.Elementi.Count > 100)
                yield return $"{what}: al massimo 100 elementi.";
            foreach (var item in set.Elementi.Where(e => Media(e.Id) == null && Promo(e.Id) == null))
                yield return $"{what}: un elemento non c'è più ({item.Id}).";
            foreach (var cash in set.Casse.Where(c => CashPoint(c) == null))
                yield return $"{what}: la cassa {cash} non c'è.";
            if (set.Giorni.Any(d => d is < 1 or > 7))
                yield return $"{what}: i giorni vanno da 1 (lunedì) a 7 (domenica).";
            if ((set.Dalle == null) != (set.Alle == null)
                || (set.Dalle != null && !(ParseTime(set.Dalle) is { } from && ParseTime(set.Alle) is { } to && from != to)))
                yield return $"{what}: gli orari sono come 18:00 e 20:00 (anche 22:00 e 02:00, a cavallo della mezzanotte).";
            if (set.DalGiorno > set.AlGiorno)
                yield return $"{what}: l'ultimo giorno viene prima del primo.";
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
    /// <summary>
    /// Il marchio Bluetech della firma in basso, dal kit ufficiale: per la firma scura (temi di notte, sopra le slide) e
    /// per quella chiara. Dove manca resta il marchio di serie; "powered by" c'è sempre.
    /// </summary>
    public string? LogoBluetech { get; set; }
    public string? LogoBluetechChiaro { get; set; }
    /// <summary>Il logo di Satispay accanto al QR del pagamento (SPEC §2.4), dal kit di Satispay; null = solo la scritta.</summary>
    public string? LogoSatispay { get; set; }
    /// <summary>Facoltativo, come per il logo del locale: la versione per i fondi chiari.</summary>
    public string? LogoSatispayChiaro { get; set; }

    /// <summary>I loghi, con il nome che hanno nei messaggi: sono tutti immagini della galleria.</summary>
    public static readonly (string Name, Func<AppearanceSettings, string?> Get, Action<AppearanceSettings, string?> Set)[] Logos =
    [
        ("il logo", a => a.Logo, (a, id) => a.Logo = id),
        ("il logo per i fondi chiari", a => a.LogoChiaro, (a, id) => a.LogoChiaro = id),
        ("il logo Bluetech", a => a.LogoBluetech, (a, id) => a.LogoBluetech = id),
        ("il logo Bluetech per i fondi chiari", a => a.LogoBluetechChiaro, (a, id) => a.LogoBluetechChiaro = id),
        ("il logo Satispay", a => a.LogoSatispay, (a, id) => a.LogoSatispay = id),
        ("il logo Satispay per i fondi chiari", a => a.LogoSatispayChiaro, (a, id) => a.LogoSatispayChiaro = id),
    ];

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

    /// <summary>Con una promozione con il QR nel Grazie, il Grazie resta fino al conto dopo, al massimo così (senza QR 8 s).</summary>
    public int SecondiGrazie { get; set; } = 30;

    /// <summary>La galleria delle promozioni: si mettono nei set.</summary>
    public List<PromoConfig> Promozioni { get; set; } = [];

    /// <summary>I set di reclame, in ordine: per ogni posto va in onda il primo che vale in quel momento.</summary>
    public List<AdSetConfig> Set { get; set; } = [];

    /// <summary>Le foto e i video caricati dal back office (le due gallerie, il logo, lo sfondo), in dati\media.</summary>
    public List<MediaItem> Media { get; set; } = [];

    /// <summary>Il QR del Grazie della prima versione: si legge solo per portarlo in una promozione e nel set Grazie.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThanksQrSettings? QrGrazie { get; set; }
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

/// <summary>Il QR del Grazie della prima versione, ora una promozione nel set Grazie.</summary>
public sealed class ThanksQrSettings
{
    public bool Attivo { get; set; }
    public string Indirizzo { get; set; } = "";
    public string Titolo { get; set; } = "";
    public string Didascalia { get; set; } = "";
    public int Secondi { get; set; } = 30;
}

/// <summary>
/// Una promozione della galleria: un titolo, un testo, un'immagine e un QR (registrazione cliente, recensioni, menu
/// digitale), tutti facoltativi ma almeno uno. Va in onda nei set che la contengono.
/// </summary>
public sealed class PromoConfig
{
    public string Id { get; set; } = "";
    /// <summary>Per il back office, non si vede sullo schermo.</summary>
    public string Nome { get; set; } = "";
    public string Titolo { get; set; } = "";
    public string Testo { get; set; } = "";
    /// <summary>Un'immagine di <see cref="AppearanceSettings.Media"/>, 16:9 come le slide.</summary>
    public string? Immagine { get; set; }
    /// <summary>L'indirizzo del QR; il PNG lo fa l'Agent. Null = senza QR.</summary>
    public string? Qr { get; set; }
    /// <summary>Sotto il QR: "Inquadra e registrati".</summary>
    public string QrDidascalia { get; set; } = "";
    /// <summary>Fuori da queste date la promozione non va in onda, in nessun set.</summary>
    public DateOnly? DalGiorno { get; set; }
    public DateOnly? AlGiorno { get; set; }

    /// <summary>I campi della prima versione (posti, immagine o video, orari), solo per portarli nei set.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Vecchio { get; set; }

    [JsonIgnore]
    public bool HasQr => !string.IsNullOrWhiteSpace(Qr);

    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Titolo) || !string.IsNullOrWhiteSpace(Testo) || Immagine != null || HasQr;
}

/// <summary>
/// Un set di reclame: foto, video e promozioni delle gallerie, in un ordine, per l'attesa (le slide a tutto schermo),
/// accanto al conto o nel Grazie, per alcune casse, giorni, orari e date. Per ogni posto va in onda il primo set
/// dell'elenco che vale in quel momento: in alto quelli con l'orario più stretto, in fondo quello di sempre.
/// </summary>
public sealed class AdSetConfig
{
    public static readonly string[] Places = ["attesa", "conto", "grazie"];
    public static readonly string[] Orders = ["elenco", "casuale", "alfabetico"];

    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public bool Attivo { get; set; } = true;
    /// <summary>"attesa", "conto", "grazie": uno o più.</summary>
    public List<string> Dove { get; set; } = [];
    /// <summary>"elenco" (come in <see cref="Elementi"/>), "casuale" o "alfabetico" (per nome).</summary>
    public string Ordine { get; set; } = "elenco";
    /// <summary>
    /// Con un numero: dopo tanti elementi senza QR, una promozione con il QR (2 = due slide, un QR, due slide, un altro
    /// QR). 0 = le promozioni con il QR sono come le altre.
    /// </summary>
    public int QrDopo { get; set; }
    /// <summary>Quanto resta una foto o una promozione, se l'elemento non dice altro. Un video dura quanto dura.</summary>
    public int Secondi { get; set; } = 8;
    public List<AdItemConfig> Elementi { get; set; } = [];
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

public sealed class AdItemConfig
{
    /// <summary>Una foto o un video di <see cref="AppearanceSettings.Media"/>, o una promozione.</summary>
    public string Id { get; set; } = "";
    /// <summary>Null = quelli del set.</summary>
    public int? Secondi { get; set; }
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
