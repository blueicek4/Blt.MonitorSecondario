namespace Bluetech.Display.Agent.Tests;

/// <summary>Le registrazioni vere in samples/registrazioni, copiate accanto ai test.</summary>
static class Samples
{
    public static readonly string[] Recordings =
    [
        "2026-10-20_002138",   // 48 colonne, 6 conti, dettagli, sconti, storni, POS
        "2026-10-20_064854",   // prova A/B/A, un conto da 26 righe
        "2026-10-20_075632",   // 30 colonne, QR Satispay, fattura, promozione
    ];

    public static string Folder(string recording) =>
        Path.Combine(AppContext.BaseDirectory, "samples", "registrazioni", recording);

    /// <summary>Le versioni di una registrazione, in ordine.</summary>
    public static List<(string Name, byte[] Bytes)> Versions(string recording) =>
        Directory.GetFiles(Folder(recording), "0*.txt")
            .Where(f => char.IsDigit(Path.GetFileName(f)[3]) && Path.GetFileName(f)[4] == '_')
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)))
            .ToList();

    /// <summary>Un file singolo in samples/, per esempio uno scontrino fotografato sul vecchio monitor.</summary>
    public static byte[] Receipt(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", name));

    public static byte[] Version(string recording, string prefix) =>
        Versions(recording).Single(v => v.Name.StartsWith(prefix + "_", StringComparison.Ordinal)).Bytes;
}
