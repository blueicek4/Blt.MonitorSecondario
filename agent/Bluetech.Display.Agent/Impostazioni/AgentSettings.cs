using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Impostazioni;

/// <summary>
/// dati\impostazioni.json: tutto quello che si cambia dal back office. Un file solo, niente database: si copia per fare
/// una copia di sicurezza o per portare la configurazione su un altro PC.
/// </summary>
public sealed partial class AgentSettings
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
    public DigitalReceiptOptions ScontrinoDigitale { get; set; } = new();

    /// <summary>I punti cassa: ognuno con il suo file di Passepartout.</summary>
    public List<CashPointConfig> Casse { get; set; } = [];

    /// <summary>Le stampanti della copia cartacea; ogni dispositivo dice quale usa.</summary>
    public List<PrinterConfig> Stampanti { get; set; } = [];

    /// <summary>I display autorizzati (Player, palmari, tablet, schermi touch), ognuno legato a una cassa.</summary>
    public List<DeviceConfig> Dispositivi { get; set; } = [];

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // accenti leggibili per chi apre il file
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Al primo avvio: una cassa sul percorso di sempre, nessuna stampante, nessun dispositivo.</summary>
    public static AgentSettings Defaults() => new()
    {
        Casse = [new CashPointConfig { Id = "cassa1", Nome = "Cassa 1", FileCassa = @"C:\MonitorSecondario\Scontrino.txt" }],
    };

    public AgentSettings Clone() => JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.SerializeToUtf8Bytes(this, Json), Json)!;

    public CashPointConfig? CashPoint(string? id) => Casse.Find(c => c.Id == id);
    public PrinterConfig? Printer(string? id) => id == null ? null : Stampanti.Find(p => p.Id == id);
    public DeviceConfig? Device(string? id) => Dispositivi.Find(d => d.Id == id);

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
        ScontrinoDigitale ??= new DigitalReceiptOptions();
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

    /// <summary>Il primo errore, in parole per chi usa il back office; null se va tutto bene.</summary>
    public string? Validate() => Problems().FirstOrDefault();

    /// <summary>
    /// Tutti gli errori. Una modifica dal back office è rifiutata solo se ne aggiunge uno: un errore che c'era già (file
    /// scritto a mano) non deve impedire di cambiare il resto, per esempio la password.
    /// </summary>
    public IEnumerable<string> Problems()
    {
        if (Rete.Porta is < 0 or > 65535)
            yield return "La porta deve essere tra 1 e 65535.";
        if (Rete.Https.Porta is < 0 or > 65535)
            yield return "La porta HTTPS deve essere tra 1 e 65535.";
        if (Rete.Https.Attivo && Rete.Https.Porta != 0 && Rete.Https.Porta == Rete.Porta)
            yield return "La porta HTTPS deve essere diversa da quella HTTP.";
        if (ChiusuraVenditaDopoSecondi is < 0 or > 3600)
            yield return "La chiusura del conto deve essere tra 0 (mai) e 3600 secondi.";
        if (ScontrinoDigitale.SecondiAVideo is < 5 or > 600)
            yield return "Lo scontrino digitale resta a video tra 5 e 600 secondi.";
        if ((ScontrinoDigitale.Dicitura?.Length ?? 0) > 200)
            yield return "La dicitura della copia è troppo lunga (al massimo 200 caratteri).";

        if (Casse.Count > 20)
            yield return "Al massimo 20 casse.";
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cash in Casse)
        {
            if (Name(cash.Nome, "La cassa") is { } error)
                yield return error;
            var file = cash.FileCassa ?? "";
            if (string.IsNullOrWhiteSpace(file) || !Path.IsPathFullyQualified(file) || Path.GetFileName(file).Length == 0)
                yield return $"{cash.Nome}: il file deve essere un percorso completo, per esempio C:\\MonitorSecondario\\Scontrino.txt.";
            else if (!string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase))
                yield return $"{cash.Nome}: il file del monitor di Passepartout è un .txt (per esempio Scontrino.txt). L'Agent lo cancella dopo averlo letto: un altro file non va bene.";
            else if (!files.Add(Path.GetFullPath(file)))
                yield return $"{cash.Nome}: il file {file} è già di un'altra cassa. Ogni cassa ha il suo file.";
        }

        if (Stampanti.Count > 50)
            yield return "Al massimo 50 stampanti.";
        foreach (var printer in Stampanti)
        {
            if (Name(printer.Nome, "La stampante") is { } error)
                yield return error;
            var address = printer.Indirizzo?.Trim() ?? "";
            if (printer.Collegamento is not ("rete" or "windows" or "prova"))
                yield return $"{printer.Nome}: il collegamento è \"rete\", \"windows\" o \"prova\".";
            else if (printer.Collegamento == "rete" && !NetworkAddress().IsMatch(address))
                yield return $"{printer.Nome}: l'indirizzo della stampante in rete è come 192.168.1.50 o 192.168.1.50:9100.";
            else if (printer.Collegamento == "windows" && address.Length == 0)
                yield return $"{printer.Nome}: manca il nome della stampante in Windows.";
            if (printer.LarghezzaMm is not (58 or 80))
                yield return $"{printer.Nome}: il rotolo è da 58 o da 80 mm.";
            if (printer.Colonne is < 24 or > 64)
                yield return $"{printer.Nome}: le colonne vanno da 24 a 64.";
            if (printer.TabellaCaratteri is not ("1252" or "858" or "ascii"))
                yield return $"{printer.Nome}: la tabella caratteri è 1252, 858 o ascii.";
            if (printer.QrPuntiPerModulo is < 1 or > 10)
                yield return $"{printer.Nome}: i punti per modulo del QR vanno da 1 a 10.";
            if (printer.TimeoutSecondi is < 1 or > 60)
                yield return $"{printer.Nome}: l'attesa della stampante va da 1 a 60 secondi.";
            if ((printer.TestoRitiro?.Length ?? 0) > 80)
                yield return $"{printer.Nome}: il testo per il ritiro è troppo lungo (al massimo 80 caratteri).";
        }

        if (Dispositivi.Count > 200)
            yield return "Al massimo 200 dispositivi.";
        foreach (var device in Dispositivi)
        {
            if (Name(device.Nome, "Il dispositivo") is { } error)
                yield return error;
            if (CashPoint(device.Cassa) == null)
                yield return $"{device.Nome}: scegli la cassa da mostrare.";
            if (device.Stampante != null && Printer(device.Stampante) == null)
                yield return $"{device.Nome}: la stampante scelta non c'è.";
            if (device.Disposizione != null && !DeviceConfig.Layouts.Contains(device.Disposizione))
                yield return $"{device.Nome}: la disposizione è automatica, larga, stretta, verticale o palmare.";
        }

        foreach (var problem in AppearanceProblems())
            yield return problem;
    }

    static string? Name(string? name, string what) =>
        string.IsNullOrWhiteSpace(name) ? $"{what} ha bisogno di un nome."
        : name.Length > 40 ? $"{what} \"{name[..20]}…\": il nome è troppo lungo (al massimo 40 caratteri)."
        : null;

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    internal static partial Regex IdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9.\-]+(:\d{1,5})?$")]
    private static partial Regex NetworkAddress();
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

public sealed class DigitalReceiptOptions
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

public sealed class CashPointConfig
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>Il file scritto dal dispositivo monitor di Passepartout per questa cassa, su un disco del PC dell'Agent.</summary>
    public string FileCassa { get; set; } = "";
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

    /// <summary>Le opzioni della stampa, con la dicitura comune a tutte le stampanti.</summary>
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

/// <summary>Un display autorizzato con il codice di abbinamento.</summary>
public sealed class DeviceConfig
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
