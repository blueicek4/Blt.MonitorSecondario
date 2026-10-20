namespace Bluetech.Display.Agent;

/// <summary>Sezione "Agent" di appsettings.json (o della riga di comando: --replay, --velocita, --ripeti).</summary>
public sealed class AgentOptions
{
    /// <summary>Il file scritto dal dispositivo monitor di Passepartout.</summary>
    public string FileCassa { get; set; } = @"C:\MonitorSecondario\Scontrino.txt";

    public int Porta { get; set; } = 5080;

    /// <summary>false: risponde solo a questo PC (il Player). true: anche a tablet e TV della rete locale.</summary>
    public bool ApertoInRete { get; set; }

    /// <summary>
    /// Pagine servite da altri indirizzi che possono collegarsi a /ws, per esempio il display in sviluppo
    /// (http://localhost:5173). Le pagine servite dall'Agent possono sempre; una pagina qualunque aperta nel browser
    /// della cassa no, altrimenti leggerebbe i conti.
    /// </summary>
    public string[] AltreOrigini { get; set; } = [];

    /// <summary>La build del display (display/dist), relativa alla cartella dell'Agent.</summary>
    public string CartellaDisplay { get; set; } = "display";

    /// <summary>
    /// Vendita aperta e nessun file per tanto tempo: si chiude. All'avvio un file più vecchio di così è un avanzo e
    /// si toglie senza mostrarlo. 0 = mai.
    /// </summary>
    public int ChiusuraVenditaDopoSecondi { get; set; } = 60;

    public string CartellaLog { get; set; } = "log";
    public int GiorniLog { get; set; } = 14;

    /// <summary>Cartella di una registrazione del registratore: la si rigioca al posto della cassa (demo, prove).</summary>
    public string? Replay { get; set; }
    public double VelocitaReplay { get; set; } = 1;
    public bool RipetiReplay { get; set; }

    /// <summary>
    /// Con <see cref="ApertoInRete"/>: le chiavi dei dispositivi abbinati (palmari, tablet). Se ce n'è almeno una, un
    /// dispositivo che non è questo PC entra solo con <c>?chiave=…</c> la prima volta, poi la ricorda. Vuoto = chiunque
    /// sia nella rete (da evitare sulla Wi-Fi degli ospiti).
    /// </summary>
    public string[] ChiaviDispositivi { get; set; } = [];

    /// <summary>Scontrino digitale a video e copia cartacea su richiesta (docs/03). Spento di default.</summary>
    public DigitalReceiptOptions ScontrinoDigitale { get; set; } = new();
}

public sealed class DigitalReceiptOptions
{
    /// <summary>Con il QR dello scontrino digitale ("QRCODESD") mostra lo scontrino, il QR grande e, se attivo, il pulsante.</summary>
    public bool MostraScontrino { get; set; }

    /// <summary>Il pulsante "Preferisci la copia di carta?": richiede <see cref="MostraScontrino"/> e una stampante.</summary>
    public bool CopiaCartacea { get; set; }

    /// <summary>Quanto resta a video lo scontrino digitale, se non parte prima un'altra vendita.</summary>
    public int SecondiAVideo { get; set; } = 45;

    public PaperCopyOptions Stampa { get; set; } = new();
}

/// <summary>La stampante comande (termica, ESC/POS) su cui l'Agent stampa la copia. Passepartout non c'entra.</summary>
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
