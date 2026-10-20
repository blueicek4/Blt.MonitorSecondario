namespace Bluetech.Display.Agent;

/// <summary>
/// Sezione "Agent" di appsettings.json (o della riga di comando): solo dove stanno le cose e le prove. Tutto il resto
/// (casse, stampanti, dispositivi, rete, password) è in dati\impostazioni.json e si cambia dal back office.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>impostazioni.json, statistiche.json e il certificato HTTPS generato, relativa alla cartella dell'Agent.</summary>
    public string CartellaDati { get; set; } = "dati";

    public string CartellaLog { get; set; } = "log";
    public int GiorniLog { get; set; } = 14;

    /// <summary>La build del display (display/dist), relativa alla cartella dell'Agent.</summary>
    public string CartellaDisplay { get; set; } = "display";

    /// <summary>
    /// Pagine servite da altri indirizzi che possono collegarsi a /ws, per esempio il display in sviluppo
    /// (http://localhost:5173). Le pagine servite dall'Agent possono sempre; una pagina qualunque aperta nel browser
    /// della cassa no, altrimenti leggerebbe i conti.
    /// </summary>
    public string[] AltreOrigini { get; set; } = [];

    /// <summary>--porta: al posto di quella delle impostazioni, solo per questo avvio (prove).</summary>
    public int? Porta { get; set; }

    /// <summary>Cartella di una registrazione del registratore: la si rigioca nella prima cassa al posto del file (demo, prove).</summary>
    public string? Replay { get; set; }
    public double VelocitaReplay { get; set; } = 1;
    public bool RipetiReplay { get; set; }
}

/// <summary>
/// Le opzioni di una stampa della copia cartacea: quelle della stampante del dispositivo (<see
/// cref="Impostazioni.PrinterConfig"/>) più la dicitura comune.
/// </summary>
public sealed class PaperCopyOptions
{
    /// <summary>"rete" (Stampante = indirizzo:porta), "windows" (Stampante = nome della coda di stampa) o "prova".</summary>
    public string Collegamento { get; set; } = "rete";

    /// <summary>"192.168.1.50:9100" (porta 9100 se manca) o il nome della stampante in Windows.</summary>
    public string Stampante { get; set; } = "";

    /// <summary>58 o 80: limita la larghezza dell'immagine del QR (384 o 576 punti).</summary>
    public int LarghezzaMm { get; set; } = 80;

    /// <summary>Caratteri per riga del font normale: 32 sui rotoli da 58 mm, 42 o 48 su quelli da 80 mm.</summary>
    public int Colonne { get; set; } = 42;

    /// <summary>"1252" (Windows-1252, ESC t 16 delle Epson), "858" (PC858, ESC t 19) o "ascii" (senza accenti).</summary>
    public string TabellaCaratteri { get; set; } = "1252";

    /// <summary>Punti per modulo del QR piccolo: 4 a 203 dpi fanno circa 25 mm di lato per il QR di Satispay.</summary>
    public int QrPuntiPerModulo { get; set; } = 4;

    public bool Taglio { get; set; } = true;

    public string Dicitura { get; set; } = "Inquadra il QR per scaricare il documento commerciale. Copia di cortesia.";

    /// <summary>Il testo a video quando la copia è partita: dove la si ritira.</summary>
    public string TestoRitiro { get; set; } = "Ritira la copia alla cassa";

    public int TimeoutSecondi { get; set; } = 8;
}
