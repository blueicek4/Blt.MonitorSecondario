using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>
/// Il link per scaricare lo scontrino digitale di Passepartout. Arriva in due modi (registrazione del 08/11/2026):
/// - dentro il PNG del QR, intatto: <c>https://scontrinodigitalecloud.passepartout.cloud/v1/download?token=…</c>;
/// - come testo in fondo allo scontrino pagato, dopo la riga <c>QRCODE</c> (il comando StampaQRCode del modello XML del
///   monitor, <c>QRCODE</c> e a capo il valore). Qui Passepartout lo "pulisce" come ogni testo per la stampante: la
///   barra diventa un trattino e il punto di domanda uno spazio, anche se il modello mette <c>/</c> tra i
///   CaratteriAmmessi: <c>https:--scontrinodigitalecloud.passepartout.cloud-v1-download token=…</c>.
/// Dal testo non si può tornare indietro in generale (un trattino può essere vero, come quelli del token), quindi si
/// riconosce solo la forma conosciuta del servizio di Passepartout e se ne ricostruisce il link. Qualunque altro testo
/// dopo <c>QRCODE</c> (un QR per le recensioni, una promozione) non è uno scontrino digitale.
/// </summary>
public static partial class DigitalReceiptLink
{
    // Schema, host *.passepartout.cloud, /v1/download, ?token= e il token (un GUID). Le varianti accettate: "//" o "--"
    // dopo lo schema; "/" o "-" nel percorso; "?" intatto, diventato spazio o sparito prima di "token=".
    [GeneratedRegex(
        @"^(?<scheme>https?):(?://|--)(?<host>(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)*passepartout\.cloud)[/-]v1[/-]download(?:\?|\s+)?token=(?<token>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Known();

    /// <summary>
    /// Il link vero da quello scritto nel file (intatto o storpiato), o null se non è il link di uno scontrino digitale
    /// di Passepartout.
    /// </summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512)
            return null;
        var match = Known().Match(text.Trim());
        if (!match.Success)
            return null;
        return $"{match.Groups["scheme"].Value.ToLowerInvariant()}://{match.Groups["host"].Value.ToLowerInvariant()}/v1/download?token={match.Groups["token"].Value}";
    }

    /// <summary>La didascalia che Passepartout scrive sotto il QR dello scontrino digitale ("Scarica il tuo scontrino digitale!").</summary>
    public static bool IsCaption(string? caption) =>
        caption != null && caption.Contains("scontrino digitale", StringComparison.OrdinalIgnoreCase);
}
