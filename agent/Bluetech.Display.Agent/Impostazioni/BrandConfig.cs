using System.Text.Json.Serialization;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// Un marchio: quello che fa sembrare il monitor del locale e non di Bluetech, e quello che vende. Lo cambia il titolare
/// dal back office e vale subito su tutte le casse del marchio. Una cassa cambia solo le scritte, uno schermo solo
/// disposizione, animazioni, attrazione e larghezza del conto.
/// </summary>
public sealed class BrandConfig
{
    public string Id { get; set; } = "";
    /// <summary>Per il back office: "La Vela · ristorante".</summary>
    public string Nome { get; set; } = "";
    public BrandIdentity Identita { get; set; } = new();
    public BrandLook Aspetto { get; set; } = new();
    public BrandScreen Schermo { get; set; } = new();
    public BrandTexts Scritte { get; set; } = new();
    public BrandSale Vendita { get; set; } = new();
    public BrandContent Contenuti { get; set; } = new();

    public MediaItem? Media(string? id) => id == null ? null : Contenuti.Media.Find(m => m.Id == id);
    public PromoConfig? Promo(string? id) => id == null ? null : Contenuti.Promozioni.Find(p => p.Id == id);
    public RuleConfig? Rule(string? id) => id == null ? null : Contenuti.Regole.Find(r => r.Id == id);
    public ShowcaseConfig? Showcase(string? id) => id == null ? null : Contenuti.Vetrine.Find(v => v.Id == id);
}

public sealed class BrandIdentity
{
    /// <summary>In alto sullo schermo, accanto al logo o al suo posto.</summary>
    public string NomeLocale { get; set; } = "";
    public string Sottotitolo { get; set; } = "";
    /// <summary>Un'immagine dei contenuti, per i fondi scuri (e sopra le slide); null = solo il nome.</summary>
    public string? Logo { get; set; }
    /// <summary>Facoltativo: la versione del logo per i fondi chiari. Un logo chiaro su un fondo chiaro non si vede.</summary>
    public string? LogoChiaro { get; set; }
}

/// <summary>Il tema e la sua forma. Si sceglie dalla sala, non dal colore: dieci temi, cinque scuri e cinque chiari.</summary>
public sealed class BrandLook
{
    public static readonly string[] Modes = ["fisso", "auto"];
    public static readonly string[] Backgrounds = ["mesh", "tinta", "sfumato", "immagine", "video"];
    public static readonly string[] Footers = ["light", "dark"];

    /// <summary>Uno dei dieci temi (Aspetto/Palettes.cs).</summary>
    public string Tema { get; set; } = "bluetech";
    /// <summary>"fisso", o "auto": il gemello chiaro di giorno (GiornoDalle–GiornoAlle), quello scuro di sera.</summary>
    public string Modo { get; set; } = "fisso";
    public string GiornoDalle { get; set; } = "07:00";
    public string GiornoAlle { get; set; } = "19:00";

    /// <summary>Solo con il modo fisso: i colori del locale al posto di quelli del tema. Null = quelli del tema.</summary>
    public ThemeColorSettings? Colori { get; set; }
    /// <summary>Una delle quattordici famiglie; null = quella del tema.</summary>
    public string? FontTitoli { get; set; }
    public string? FontTesto { get; set; }
    /// <summary>Angoli, 0–32 px; null = quelli del tema.</summary>
    public int? Raggio { get; set; }
    /// <summary>Vetro della card, 0–100, solo con il modo fisso; null = quello del tema.</summary>
    public int? Vetro { get; set; }
    /// <summary>"light" (la firma chiara, per i fondi scuri) o "dark"; null = quella del tema.</summary>
    public string? Footer { get; set; }

    /// <summary>"mesh" (le luci del tema), "tinta", "sfumato", "immagine" o "video" (con <see cref="FondoMedia"/>).</summary>
    public string Fondo { get; set; } = "mesh";
    public string? FondoMedia { get; set; }
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

/// <summary>Come si comporta lo schermo. Ogni schermo può cambiare animazioni, attrazione, larghezza e allineamento.</summary>
public sealed class BrandScreen
{
    /// <summary>Quanto la macchina regge: "full", "light" (consigliate) o "eco". Nel pannello: Piena, Leggera, Eco.</summary>
    public static readonly string[] MotionLevels = ["full", "light", "eco"];
    /// <summary>Quanto il monitor vende (SPEC §14): sobrio, vivo, spettacolo, troppo.</summary>
    public static readonly string[] AttractionLevels = ["sobrio", "vivo", "spettacolo", "troppo"];
    public static readonly string[] Alignments = ["alto", "centro", "basso"];
    public static readonly string[] QrLayouts = ["beside", "fullscreen"];

    public string Animazioni { get; set; } = "light";
    public string Attrazione { get; set; } = "vivo";
    /// <summary>La card del conto su 24 colonne: 9–17 (a 1366 e 1280 al massimo 16, lo applica lo schermo). La colonna accanto prende il resto.</summary>
    public int ColonneConto { get; set; } = 16;
    /// <summary>Su 24, mentre il QR di pagamento è accanto al conto: 8–14.</summary>
    public int ColonneContoPagamento { get; set; } = 10;
    /// <summary>Dove sta la colonna accanto al conto: "alto", "centro" o "basso". Non vale in verticale né sul palmare.</summary>
    public string ColonnaAllineamento { get; set; } = "basso";
    /// <summary>"beside" (accanto al conto, che si stringe) o "fullscreen". In verticale e sul palmare sempre a tutto schermo.</summary>
    public string QrPagamento { get; set; } = "beside";
    /// <summary>
    /// Importi e totale solo quando la cassa mostra il conto (preconto, scontrino), non durante la battuta: come
    /// ShowPricesDuringOrder=false del vecchio monitor 6.0. I ruoli si invertono (SPEC §2.2.1).
    /// </summary>
    public bool PrezziSoloSulConto { get; set; }
    /// <summary>Righe più grandi sul formato stretto (due righe in meno a video).</summary>
    public bool RigheCompatte { get; set; }
}

public sealed class BrandTexts
{
    /// <summary>Nell'attesa quando non ci sono slide.</summary>
    public string Benvenuto { get; set; } = "Benvenuti";
    /// <summary>Il testo che scorre in basso nell'attesa; vuoto = niente.</summary>
    public string Scorrevole { get; set; } = "";
    public string TitoloConto { get; set; } = "Il tuo conto";
    public string Grazie { get; set; } = "Grazie e arrivederci";
    /// <summary>Con una promozione con il QR nel Grazie, il Grazie resta fino al conto dopo, al massimo così (senza QR 8 s).</summary>
    public int SecondiGrazie { get; set; } = 30;
}

public sealed class BrandSale
{
    /// <summary>
    /// Vendita aperta e nessun file per tanto tempo: si chiude e il display torna all'attesa. All'avvio un file più
    /// vecchio di così è un avanzo e si toglie senza mostrarlo. 0 = mai.
    /// </summary>
    public int ChiusuraDopoSecondi { get; set; } = 60;
    public DigitalReceiptConfig ScontrinoDigitale { get; set; } = new();
}

/// <summary>Lo scontrino digitale a video e la copia di carta su richiesta (SPEC §2.7).</summary>
public sealed class DigitalReceiptConfig
{
    /// <summary>Con il QR dello scontrino digitale ("QRCODESD"), dopo il Grazie, lo stato scontrino: carta, QR, reclame.</summary>
    public bool Attivo { get; set; }
    /// <summary>Quanto resta a video, se non parte prima un'altra vendita.</summary>
    public int Secondi { get; set; } = 45;
    /// <summary>Il pulsante della copia di carta, sui soli schermi touch con una stampante.</summary>
    public bool CopiaCarta { get; set; }
    /// <summary>In fondo alla copia, sotto il QR piccolo.</summary>
    public string Dicitura { get; set; } = "Inquadra il QR per scaricare il documento commerciale. Copia di cortesia.";
    /// <summary>I tre blocchi sui sei posti (3 colonne × 2 righe). Default: carta in colonna 1, QR in colonna 2, reclame spenta.</summary>
    public List<ReceiptBlock> Blocchi { get; set; } = ReceiptBlock.Defaults();
}

/// <summary>
/// Un blocco dello scontrino digitale e il suo posto. Vincoli (li controlla il salvataggio): la carta sempre "piena";
/// due blocchi nella stessa colonna solo "alto" + "basso"; almeno un blocco acceso.
/// </summary>
public sealed class ReceiptBlock
{
    public static readonly string[] Types = ["carta", "qr", "reclame"];
    public static readonly string[] Rows = ["alto", "basso", "piena"];

    public string Tipo { get; set; } = "";
    public bool Attivo { get; set; }
    public int Colonna { get; set; } = 1;
    public string Riga { get; set; } = "piena";

    public static List<ReceiptBlock> Defaults() =>
    [
        new() { Tipo = "carta", Attivo = true, Colonna = 1, Riga = "piena" },
        new() { Tipo = "qr", Attivo = true, Colonna = 2, Riga = "piena" },
        new() { Tipo = "reclame", Attivo = false, Colonna = 3, Riga = "piena" },
    ];
}

/// <summary>Cosa gira (le gallerie) e quando (le regole), messo in vetrina per ogni posto.</summary>
public sealed class BrandContent
{
    /// <summary>Le foto e i video caricati (le gallerie, i loghi, il fondo), in dati\media.</summary>
    public List<MediaItem> Media { get; set; } = [];
    public List<PromoConfig> Promozioni { get; set; } = [];
    /// <summary>Riusabili: "il venerdì sera" serve a più vetrine.</summary>
    public List<RuleConfig> Regole { get; set; } = [];
    /// <summary>In ordine di priorità: per ogni posto va in onda la prima che vale in quel momento.</summary>
    public List<ShowcaseConfig> Vetrine { get; set; } = [];
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
    /// <summary>Il punto della foto che sopravvive a ogni ritaglio (0–1); null = il centro.</summary>
    public FocusPoint? Focus { get; set; }
    /// <summary>La risposta a "questa foto ha già delle scritte dentro?": decide il modello B (parlante) o C (muta).</summary>
    public bool HaScritte { get; set; }
    /// <summary>Per un video: il primo fotogramma, fatto al caricamento (per Eco e per i fondi fermi).</summary>
    public string? Poster { get; set; }
    /// <summary>Per un video: il primo fotogramma già sfocato e scurito: il display non sfoca mai a runtime.</summary>
    public string? PosterSfocato { get; set; }
    /// <summary>Il file è un derivato di un video (Poster, PosterSfocato): non compare nelle gallerie.</summary>
    public bool Derivato { get; set; }

    [JsonIgnore]
    public bool IsVideo => Tipo.StartsWith("video/", StringComparison.Ordinal);
}

public sealed class FocusPoint
{
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
}

/// <summary>
/// Una promozione della galleria: un titolo, un testo, un'immagine, un QR (registrazione cliente, recensioni, menu
/// digitale), un modello (SPEC §16) e un'etichetta. Esiste una volta: usata in più vetrine, cambiarla le cambia tutte.
/// </summary>
public sealed class PromoConfig
{
    public static readonly string[] Models = ["A", "B", "C", "D"];
    /// <summary>Le etichette sopra il titolo: una scelta, non un campo libero. "nessuna" = senza.</summary>
    public static readonly string[] Labels = ["promozione", "novita", "settimana", "consiglio", "evento", "cliente", "nessuna"];

    public string Id { get; set; } = "";
    /// <summary>Per il back office, non si vede sullo schermo.</summary>
    public string Nome { get; set; } = "";
    public string Etichetta { get; set; } = "promozione";
    public string Titolo { get; set; } = "";
    public string Testo { get; set; } = "";
    /// <summary>Un'immagine dei contenuti, 16:9 come le slide.</summary>
    public string? Immagine { get; set; }
    /// <summary>
    /// A solo testo · B foto parlante (il sistema non aggiunge il titolo) · C foto muta (titolo su scrim laterale) · D con
    /// QR. Lo propone il back office dalla risposta "la foto ha già delle scritte?".
    /// </summary>
    public string Modello { get; set; } = "A";
    /// <summary>L'indirizzo del QR; il PNG lo fa l'Agent. Null = senza QR.</summary>
    public string? Qr { get; set; }
    /// <summary>Sotto il QR: "Inquadra e registrati". Al massimo due righe.</summary>
    public string QrDidascalia { get; set; } = "";
    /// <summary>Fuori da queste date la promozione non va in onda, in nessuna vetrina.</summary>
    public DateOnly? DalGiorno { get; set; }
    public DateOnly? AlGiorno { get; set; }

    [JsonIgnore]
    public bool HasQr => !string.IsNullOrWhiteSpace(Qr);

    [JsonIgnore]
    public bool HasContent => !string.IsNullOrWhiteSpace(Titolo) || !string.IsNullOrWhiteSpace(Testo) || Immagine != null || HasQr;

    /// <summary>Il modello che la promozione avrebbe se non lo sceglie nessuno: dal QR, dall'immagine e dalle sue scritte.</summary>
    public static string Suggested(bool hasQr, MediaItem? image) => hasQr ? "D" : image == null ? "A" : image.HaScritte ? "B" : "C";
}

/// <summary>Quando: casse, giorni, ore e date. Riusabile fra vetrine.</summary>
public sealed class RuleConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
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

/// <summary>
/// Una vetrina (era "set"): foto, video e promozioni delle gallerie, in un ordine, per uno o più posti (l'attesa, accanto
/// al conto, il Grazie), con una regola. Per ogni posto va in onda la prima vetrina valida dell'elenco.
/// </summary>
public sealed class ShowcaseConfig
{
    public static readonly string[] Places = ["attesa", "conto", "grazie"];
    public static readonly string[] Rotations = ["elenco", "casuale", "alfabetico"];

    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public bool Attiva { get; set; } = true;
    /// <summary>"attesa", "conto", "grazie": uno o più.</summary>
    public List<string> Posti { get; set; } = [];
    /// <summary>Id di una regola; null = sempre, in tutte le casse.</summary>
    public string? Regola { get; set; }
    /// <summary>"elenco" (come in <see cref="Elementi"/>), "casuale" o "alfabetico" (per nome).</summary>
    public string Rotazione { get; set; } = "elenco";
    /// <summary>Dopo tanti elementi senza QR, una promozione con il QR (2 = due slide, un QR…). 0 = come le altre.</summary>
    public int QrOgni { get; set; }
    /// <summary>Quanto resta una foto o una promozione, se l'elemento non dice altro. Un video dura quanto dura; un QR almeno 15 s.</summary>
    public int Secondi { get; set; } = 7;
    public List<ShowcaseItemConfig> Elementi { get; set; } = [];
}

public sealed class ShowcaseItemConfig
{
    /// <summary>Una foto o un video dei contenuti, o una promozione.</summary>
    public string Id { get; set; } = "";
    /// <summary>Null = quelli della vetrina.</summary>
    public int? Secondi { get; set; }
}
