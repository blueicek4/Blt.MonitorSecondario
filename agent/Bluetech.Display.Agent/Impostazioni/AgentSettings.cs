using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// dati\impostazioni.json, versione 3: un file solo, niente database, si copia per fare una copia di sicurezza o per
/// portare la configurazione su un altro PC. Quattro livelli (design, iterazione 2, config.ts): l'installazione (solo il
/// tecnico), i marchi (aspetto, schermo, scritte, vendita e contenuti di un locale), le casse (ognuna di un marchio) e
/// gli schermi (ognuno di una cassa, con le sue eccezioni). Le stampanti a parte. L'eredità la risolve l'Agent: ogni
/// schermo riceve la sua configurazione già piatta (<see cref="Aspetto.ScreenConfig"/>).
/// </summary>
public sealed partial class AgentSettings
{
    public const int CurrentVersion = 3;

    /// <summary>3. Un file senza versione è della 2 (o della prima): si porta alla 3 all'avvio, con una copia.</summary>
    public int Versione { get; set; } = CurrentVersion;

    public InstallationSettings Installazione { get; set; } = new();

    /// <summary>I marchi dell'installazione: di solito uno, due per un bar e un ristorante nello stesso edificio.</summary>
    public List<BrandConfig> Marchi { get; set; } = [];

    /// <summary>I punti cassa: ognuno con il suo file di Passepartout e il suo marchio.</summary>
    public List<CashPointConfig> Casse { get; set; } = [];

    /// <summary>Gli schermi abbinati (Player, palmari, tablet, monitor touch), ognuno legato a una cassa.</summary>
    public List<ScreenSettings> Schermi { get; set; } = [];

    /// <summary>Le stampanti della copia cartacea; ogni schermo dice quale usa.</summary>
    public List<PrinterConfig> Stampanti { get; set; } = [];

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // accenti leggibili per chi apre il file
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Al primo avvio: un marchio, una cassa sul percorso di sempre, nessuna stampante, nessuno schermo.</summary>
    public static AgentSettings Defaults() => new()
    {
        Marchi = [new BrandConfig { Id = "locale", Nome = "Il locale" }],
        Casse = [new CashPointConfig { Id = "cassa1", Nome = "Cassa 1", Marchio = "locale", FileCassa = @"C:\MonitorSecondario\Scontrino.txt" }],
    };

    public AgentSettings Clone() => JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.SerializeToUtf8Bytes(this, Json), Json)!;

    public CashPointConfig? CashPoint(string? id) => id == null ? null : Casse.Find(c => c.Id == id);
    public PrinterConfig? Printer(string? id) => id == null ? null : Stampanti.Find(p => p.Id == id);
    public ScreenSettings? Screen(string? id) => id == null ? null : Schermi.Find(d => d.Id == id);
    public BrandConfig? Brand(string? id) => id == null ? null : Marchi.Find(m => m.Id == id);

    /// <summary>Il marchio di una cassa; il primo se la cassa non ce l'ha (un file scritto a mano), mai null se ce n'è uno.</summary>
    public BrandConfig BrandOf(CashPointConfig? cash) => Brand(cash?.Marchio) ?? Marchi[0];

    /// <summary>Il prossimo id libero: cassa1, cassa2…</summary>
    public static string NextId(string prefix, IEnumerable<string> used)
    {
        var max = 0;
        foreach (var id in used)
            if (id != null && id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], out var n))
                max = Math.Max(max, n);
        return prefix + (max + 1);
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

    [GeneratedRegex(@"^[A-Za-z0-9.\-]+(:\d{1,5})?$")]
    private static partial Regex NetworkAddress();
}

/// <summary>Il livello del tecnico: vale al riavvio dell'Agent (la rete) o subito (il resto). Il titolare non lo vede.</summary>
public sealed class InstallationSettings
{
    public NetworkSettings Rete { get; set; } = new();

    /// <summary>
    /// false (di default): il back office si apre solo dal PC dell'Agent, anche in desktop remoto. true: anche dagli altri
    /// PC e dai telefoni della rete del locale, che a volte è la stessa del Wi-Fi dei clienti. Vale subito.
    /// </summary>
    public bool BackOfficeDallaRete { get; set; }

    /// <summary>
    /// Le password del back office, una per ruolo (titolare, tecnico), come hash. Senza quella del tecnico il titolare
    /// vede anche l'installazione, come prima dei due ruoli: si separano impostando quella del tecnico.
    /// </summary>
    public List<AccessEntry> Accessi { get; set; } = [];

    /// <summary>
    /// Modalità demo: dopo ogni conto pagato, se la cassa non manda il QR dello scontrino digitale, l'Agent ne fa uno
    /// finto (porta alla pagina di prova dello scontrino) e mostra lo scontrino digitale, con la copia di carta dove c'è
    /// una stampante. Per provare senza il servizio vero.
    /// </summary>
    public bool Demo { get; set; }

    /// <summary>I loghi ufficiali di Bluetech (firma in basso) e di Satispay (pagamento), dai loro kit.</summary>
    public KitLogos Loghi { get; set; } = new();

    /// <summary>I file dei loghi dei kit, in dati\media come quelli dei marchi.</summary>
    public List<MediaItem> Media { get; set; } = [];

    public PasswordHash? Password(string role) => Accessi.Find(a => a.Ruolo == role)?.Password;

    /// <summary>La password di un ruolo al posto di quella di prima; null la toglie (solo quella del tecnico).</summary>
    public void SetPassword(string role, PasswordHash? hash)
    {
        Accessi.RemoveAll(a => a.Ruolo == role);
        if (hash != null)
            Accessi.Add(new AccessEntry { Ruolo = role, Password = hash });
    }
}

public sealed class AccessEntry
{
    public const string Owner = "titolare";
    public const string Technician = "tecnico";
    public static readonly string[] Roles = [Owner, Technician];

    public string Ruolo { get; set; } = Owner;
    public PasswordHash Password { get; set; } = new();
}

/// <summary>
/// I loghi dei kit, id di file in <see cref="InstallationSettings.Media"/>. Bluetech per i fondi scuri (la firma chiara,
/// anche sopra le slide) e per quelli chiari: dove manca resta il marchio di serie, "powered by" c'è sempre. Satispay
/// accanto al QR del pagamento; sui fondi chiari quello chiaro se c'è, altrimenti l'altro.
/// </summary>
public sealed class KitLogos
{
    public string? Bluetech { get; set; }
    public string? BluetechChiaro { get; set; }
    public string? Satispay { get; set; }
    public string? SatispayChiaro { get; set; }

    /// <summary>I quattro, con il nome che hanno nei messaggi.</summary>
    public static readonly (string Name, Func<KitLogos, string?> Get, Action<KitLogos, string?> Set)[] All =
    [
        ("il logo Bluetech", l => l.Bluetech, (l, id) => l.Bluetech = id),
        ("il logo Bluetech per i fondi chiari", l => l.BluetechChiaro, (l, id) => l.BluetechChiaro = id),
        ("il logo Satispay", l => l.Satispay, (l, id) => l.Satispay = id),
        ("il logo Satispay per i fondi chiari", l => l.SatispayChiaro, (l, id) => l.SatispayChiaro = id),
    ];
}

/// <summary>PBKDF2 con SHA-256: nel file non c'è la password, solo come verificarla.</summary>
public sealed class PasswordHash
{
    public int Iterazioni { get; set; }
    public string Sale { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class NetworkSettings
{
    /// <summary>La porta HTTP. Cambia al riavvio dell'Agent.</summary>
    public int Porta { get; set; } = 5080;

    /// <summary>false: risponde solo a questo PC. true: anche ai dispositivi della rete (serve una regola nel firewall).</summary>
    public bool ApertoInRete { get; set; }

    public HttpsSettings Https { get; set; } = new();
}

/// <summary>HTTPS su una seconda porta, accanto a HTTP. Cambia al riavvio dell'Agent.</summary>
public sealed class HttpsSettings
{
    public bool Attivo { get; set; }
    public int Porta { get; set; } = 5443;

    /// <summary>Un certificato .pfx; vuoto = lo genera l'Agent (dati\https), con un'autorità da installare sui dispositivi.</summary>
    public string Certificato { get; set; } = "";
    public string PasswordCertificato { get; set; } = "";
}

public sealed class CashPointConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>Il marchio che i suoi schermi mostrano.</summary>
    public string Marchio { get; set; } = "";

    /// <summary>Il file scritto dal dispositivo monitor di Passepartout per questa cassa, su un disco del PC dell'Agent. Solo il tecnico lo vede.</summary>
    public string FileCassa { get; set; } = "";

    /// <summary>Le scritte di questa cassa al posto di quelle del marchio (un benvenuto diverso al bar).</summary>
    public CashOverride? Override { get; set; }
}

public sealed class CashOverride
{
    public TextsOverride? Scritte { get; set; }
}

/// <summary>Le scritte che una cassa può cambiare; null = quella del marchio.</summary>
public sealed class TextsOverride
{
    public string? Benvenuto { get; set; }
    public string? Scorrevole { get; set; }
    public string? TitoloConto { get; set; }
    public string? Grazie { get; set; }
}

/// <summary>Una stampante comande (termica, ESC/POS) su cui l'Agent stampa la copia cartacea. Passepartout non c'entra.</summary>
public sealed class PrinterConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>"rete" (Indirizzo = ip o nome, porta 9100 se manca), "windows" (Indirizzo = nome della coda) o "prova".</summary>
    public string Collegamento { get; set; } = "rete";
    public string Indirizzo { get; set; } = "";

    /// <summary>58 o 80: limita la larghezza dell'immagine del QR (384 o 576 punti).</summary>
    public int LarghezzaMm { get; set; } = 80;

    /// <summary>Caratteri per riga del font normale: 32 sui rotoli da 58 mm, 42 o 48 su quelli da 80 mm.</summary>
    public int Colonne { get; set; } = 42;

    /// <summary>"1252" (Windows-1252, ESC t 16 delle Epson), "858" (PC858, ESC t 19) o "ascii" (senza accenti).</summary>
    public string TabellaCaratteri { get; set; } = "1252";

    /// <summary>Punti per modulo del QR piccolo: 4 a 203 dpi fanno circa 25 mm di lato per il QR di Satispay.</summary>
    public int QrPuntiPerModulo { get; set; } = 4;

    public bool Taglio { get; set; } = true;

    /// <summary>Il testo a video quando la copia è partita: dove la si ritira.</summary>
    public string TestoRitiro { get; set; } = "Ritira la copia alla cassa";

    public int TimeoutSecondi { get; set; } = 8;

    /// <summary>Le opzioni della stampa, con la dicitura del marchio.</summary>
    public PaperCopyOptions ToOptions(string notice) => new()
    {
        Collegamento = Collegamento,
        Stampante = Indirizzo,
        LarghezzaMm = LarghezzaMm,
        Colonne = Colonne,
        TabellaCaratteri = TabellaCaratteri,
        QrPuntiPerModulo = QrPuntiPerModulo,
        Taglio = Taglio,
        Dicitura = notice,
        TestoRitiro = TestoRitiro,
        TimeoutSecondi = TimeoutSecondi,
    };
}

/// <summary>Uno schermo autorizzato con il codice di abbinamento (era "dispositivo").</summary>
public sealed class ScreenSettings
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>SHA-256 del token nel cookie dello schermo: il token vero lo ha solo lo schermo.</summary>
    public string Token { get; set; } = "";

    /// <summary>La cassa che lo schermo mostra.</summary>
    public string Cassa { get; set; } = "";

    /// <summary>Dove stampa la copia cartacea chiesta da questo schermo; null = niente pulsante.</summary>
    public string? Stampante { get; set; }

    public DateTimeOffset AutorizzatoIl { get; set; }

    /// <summary>Quello che vale solo per questo schermo; ogni valore null = quello del marchio.</summary>
    public ScreenOverride? Override { get; set; }
}

/// <summary>
/// Le eccezioni di uno schermo: dipendono dal pezzo di vetro, non dal locale. Un iPad Pro è 4:3 ma regge la disposizione
/// larga; un palmare vecchio vuole le animazioni leggere.
/// </summary>
public sealed class ScreenOverride
{
    /// <summary>"wide", "narrow", "portrait" o "palmare"; null = dal rapporto dei lati.</summary>
    public string? Disposizione { get; set; }
    public string? Animazioni { get; set; }
    public string? Attrazione { get; set; }
    public int? ColonneConto { get; set; }
    public string? ColonnaAllineamento { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Disposizione == null && Animazioni == null && Attrazione == null && ColonneConto == null && ColonnaAllineamento == null;

    public static readonly string[] Layouts = ["wide", "narrow", "portrait", "palmare"];
}
