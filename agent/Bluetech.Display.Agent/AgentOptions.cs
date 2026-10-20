namespace Bluetech.Display.Agent;

/// <summary>Sezione "Agent" di appsettings.json (o della riga di comando: --replay, --velocita, --ripeti).</summary>
public sealed class AgentOptions
{
    /// <summary>Il file scritto dal dispositivo monitor di Passepartout.</summary>
    public string FileCassa { get; set; } = @"C:\MonitorSecondario\Scontrino.txt";

    public int Porta { get; set; } = 5080;

    /// <summary>false: risponde solo a questo PC (il Player). true: anche a tablet e TV della rete locale.</summary>
    public bool ApertoInRete { get; set; }

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
}
