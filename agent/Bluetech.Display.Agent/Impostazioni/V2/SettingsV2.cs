using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Impostazioni.V2;

/// <summary>
/// impostazioni.json com'era fino alla versione 2 (un aspetto solo, i set, i dispositivi): serve solo a leggere un file
/// vecchio e portarlo alla versione 3 (<see cref="MigrazioneV2"/>). La riparazione è quella di allora, compresa la
/// conversione delle promozioni della prima versione.
/// </summary>
public sealed partial class SettingsV2
{
    /// <summary>La password del back office, come hash. Null: si imposta dal back office aperto sul PC dell'Agent.</summary>
    public PasswordHash? Password { get; set; }

    public NetworkSettings Rete { get; set; } = new();

    /// <summary>
    /// false (di default): il back office si apre solo dal PC dell'Agent, anche in desktop remoto. true: anche dagli altri
    /// PC e dai telefoni della rete del locale, che a volte è la stessa del Wi-Fi dei clienti. Vale subito.
    /// </summary>
    public bool BackOfficeDallaRete { get; set; }

    /// <summary>
    /// Vendita aperta e nessun file per tanto tempo: si chiude e il display torna alle slide. All'avvio un file più
    /// vecchio di così è un avanzo e si toglie senza mostrarlo. 0 = mai.
    /// </summary>
    public int ChiusuraVenditaDopoSecondi { get; set; } = 60;

    /// <summary>Scontrino digitale a video e copia cartacea su richiesta (docs/03). Spento di default.</summary>
    public DigitalReceiptV2 ScontrinoDigitale { get; set; } = new();

    /// <summary>I punti cassa: ognuno con il suo file di Passepartout.</summary>
    public List<CashPointV2> Casse { get; set; } = [];

    /// <summary>Le stampanti della copia cartacea; ogni dispositivo dice quale usa.</summary>
    public List<PrinterConfig> Stampanti { get; set; } = [];

    /// <summary>I display autorizzati (Player, palmari, tablet, schermi touch), ognuno legato a una cassa.</summary>
    public List<DeviceV2> Dispositivi { get; set; } = [];

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // accenti leggibili per chi apre il file
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public CashPointV2? CashPoint(string? id) => Casse.Find(c => c.Id == id);
    public PrinterConfig? Printer(string? id) => id == null ? null : Stampanti.Find(p => p.Id == id);
    public DeviceV2? Device(string? id) => Dispositivi.Find(d => d.Id == id);

    /// <summary>Il prossimo id libero: cassa1, cassa2…</summary>
    public static string NextId(string prefix, IEnumerable<string> used)
    {
        var max = 0;
        foreach (var id in used)
            if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], out var n))
                max = Math.Max(max, n);
        return prefix + (max + 1);
    }

    /// <summary>
    /// Sistema un file scritto a mano: liste mancanti, id mancanti o doppi, riferimenti a stampanti o casse che non ci
    /// sono più. Restituisce cosa ha cambiato, per il log.
    /// </summary>
    public List<string> Repair()
    {
        var notes = new List<string>();
        Rete ??= new NetworkSettings();
        Rete.Https ??= new HttpsSettings();
        ScontrinoDigitale ??= new DigitalReceiptV2();
        Casse ??= [];
        Stampanti ??= [];
        Dispositivi ??= [];
        Casse.RemoveAll(c => c == null);
        Stampanti.RemoveAll(p => p == null);
        Dispositivi.RemoveAll(d => d == null);

        FixIds(Casse, "cassa", c => c.Id, (c, id) => c.Id = id, notes);
        FixIds(Stampanti, "stampante", p => p.Id, (p, id) => p.Id = id, notes);
        FixIds(Dispositivi, "dispositivo", d => d.Id, (d, id) => d.Id = id, notes);

        foreach (var device in Dispositivi)
        {
            if (device.Stampante != null && Printer(device.Stampante) == null)
            {
                notes.Add($"{device.Nome}: la stampante {device.Stampante} non c'è più");
                device.Stampante = null;
            }
            if (CashPoint(device.Cassa) == null && Casse.Count > 0)
            {
                notes.Add($"{device.Nome}: la cassa {device.Cassa} non c'è più, passa a {Casse[0].Nome}");
                device.Cassa = Casse[0].Id;
            }
        }
        RepairAppearance(notes);
        return notes;
    }

    static void FixIds<T>(List<T> items, string prefix, Func<T, string> get, Action<T, string> set, List<string> notes)
    {
        var seen = new HashSet<string>();
        foreach (var item in items)
        {
            var id = get(item);
            if (string.IsNullOrWhiteSpace(id) || !IdPattern().IsMatch(id) || !seen.Add(id))
            {
                var fresh = NextId(prefix, items.Select(get).Where(i => i != null).Concat(seen));
                set(item, fresh);
                seen.Add(fresh);
                notes.Add($"id \"{id}\" sostituito con {fresh}");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    internal static partial Regex IdPattern();
}

public sealed class DigitalReceiptV2
{
    /// <summary>Con il QR dello scontrino digitale ("QRCODESD") mostra lo scontrino, il QR grande e, se attivo, il pulsante.</summary>
    public bool MostraScontrino { get; set; }

    /// <summary>Il pulsante "Preferisci la copia di carta?" sui dispositivi che hanno una stampante.</summary>
    public bool CopiaCartacea { get; set; }

    /// <summary>Quanto resta a video lo scontrino digitale, se non parte prima un'altra vendita.</summary>
    public int SecondiAVideo { get; set; } = 45;

    /// <summary>In fondo alla copia, sotto il QR piccolo.</summary>
    public string Dicitura { get; set; } = "Inquadra il QR per scaricare il documento commerciale. Copia di cortesia.";
}

public sealed class CashPointV2
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>Il file scritto dal dispositivo monitor di Passepartout per questa cassa, su un disco del PC dell'Agent.</summary>
    public string FileCassa { get; set; } = "";
}

/// <summary>Un display autorizzato con il codice di abbinamento.</summary>
public sealed class DeviceV2
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>SHA-256 del token nel cookie del dispositivo: il token vero lo ha solo il dispositivo.</summary>
    public string Token { get; set; } = "";

    /// <summary>La cassa che il dispositivo mostra.</summary>
    public string Cassa { get; set; } = "";

    /// <summary>Dove stampa la copia cartacea chiesta da questo dispositivo; null = niente pulsante.</summary>
    public string? Stampante { get; set; }

    /// <summary>
    /// Null: la disposizione la sceglie lo schermo dal rapporto d'aspetto (SPEC §9 del design). Altrimenti forzata:
    /// "larga" (il conto con la colonna accanto, per esempio su un iPad Pro, che è 4:3), "stretta" (il conto a tutta
    /// larghezza), "verticale", "palmare".
    /// </summary>
    public string? Disposizione { get; set; }

    public static readonly string[] Layouts = ["larga", "stretta", "verticale", "palmare"];

    public DateTimeOffset AutorizzatoIl { get; set; }
}

public sealed partial class SettingsV2
{
    /// <summary>Come appare lo schermo del cliente: logo, tema, scritte, le gallerie e i set di reclame.</summary>
    public AppearanceV2 Aspetto { get; set; } = new();

    public MediaV2? Media(string? id) => id == null ? null : Aspetto.Media.Find(m => m.Id == id);
    public PromoV2? Promo(string? id) => id == null ? null : Aspetto.Promozioni.Find(p => p.Id == id);

    void RepairAppearance(List<string> notes)
    {
        Aspetto ??= new AppearanceV2();
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
        foreach (var (name, get, set) in AppearanceV2.Logos)
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

        var idle = new AdSetV2 { Nome = "Attesa", Dove = ["attesa"] };
        var sale = new AdSetV2 { Nome = "Accanto al conto", Dove = ["conto"] };
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
                idle.Elementi.Add(new AdItemV2 { Id = media.Id, Secondi = media.IsVideo ? null : Math.Clamp(seconds, 3, 120) });
            if (Flag("NelConto", false) && (words || promo.Immagine != null))
                sale.Elementi.Add(new AdItemV2 { Id = words ? promo.Id : promo.Immagine! });
        }
        // Senza titolo né testo una promozione era solo la sua slide: ora è la foto o il video nel set.
        a.Promozioni.RemoveAll(p => old.Contains(p) && string.IsNullOrWhiteSpace(p.Titolo) && string.IsNullOrWhiteSpace(p.Testo));

        var sets = new List<AdSetV2> { idle, sale };
        if (!string.IsNullOrEmpty(thanksUrl))
        {
            var qr = new PromoV2
            {
                Id = NextId("promo", a.Promozioni.Select(p => p.Id)),
                Nome = "QR dopo il pagamento",
                Titolo = thanks!.Titolo?.Trim() ?? "",
                Qr = thanksUrl,
                QrDidascalia = thanks.Didascalia?.Trim() ?? "",
            };
            a.Promozioni.Add(qr);
            sets.Add(new AdSetV2 { Nome = "Grazie", Dove = ["grazie"], Attivo = thanks.Attivo, Elementi = [new AdItemV2 { Id = qr.Id }] });
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

    /// <summary>"07:30" → 7:30; null se non è un'ora.</summary>
    public static TimeOnly? ParseTime(string? text) =>
        TimeOnly.TryParseExact(text?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    [GeneratedRegex(@"^[0-9a-f]{20}\.(png|jpg|gif|webp|mp4|webm)$")]
    internal static partial Regex MediaIdPattern();
}

public sealed class AppearanceV2
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
    public static readonly (string Name, Func<AppearanceV2, string?> Get, Action<AppearanceV2, string?> Set)[] Logos =
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
    public ColorsV2? Colori { get; set; }
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
    public List<PromoV2> Promozioni { get; set; } = [];

    /// <summary>I set di reclame, in ordine: per ogni posto va in onda il primo che vale in quel momento.</summary>
    public List<AdSetV2> Set { get; set; } = [];

    /// <summary>Le foto e i video caricati dal back office (le due gallerie, il logo, lo sfondo), in dati\media.</summary>
    public List<MediaV2> Media { get; set; } = [];

    /// <summary>Il QR del Grazie della prima versione: si legge solo per portarlo in una promozione e nel set Grazie.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThanksQrV2? QrGrazie { get; set; }
}

/// <summary>Gli otto colori dei token del design, con i nomi del back office.</summary>
public sealed class ColorsV2
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
public sealed class ThanksQrV2
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
public sealed class PromoV2
{
    public string Id { get; set; } = "";
    /// <summary>Per il back office, non si vede sullo schermo.</summary>
    public string Nome { get; set; } = "";
    public string Titolo { get; set; } = "";
    public string Testo { get; set; } = "";
    /// <summary>Un'immagine di <see cref="AppearanceV2.Media"/>, 16:9 come le slide.</summary>
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
public sealed class AdSetV2
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
    public List<AdItemV2> Elementi { get; set; } = [];
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

public sealed class AdItemV2
{
    /// <summary>Una foto o un video di <see cref="AppearanceV2.Media"/>, o una promozione.</summary>
    public string Id { get; set; } = "";
    /// <summary>Null = quelli del set.</summary>
    public int? Secondi { get; set; }
}

public sealed class MediaV2
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
