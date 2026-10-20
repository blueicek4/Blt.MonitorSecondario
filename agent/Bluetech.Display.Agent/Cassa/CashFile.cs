using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Agent.Cassa;

public enum CashFileKind
{
    /// <summary>File vuoto o con la prima riga vuota: si ignora, come il vecchio monitor.</summary>
    Empty,
    /// <summary>Prima riga "SNF": scontrino.</summary>
    Receipt,
    /// <summary>
    /// "SNF", una riga "QRCODE" (pagamento Satispay) o "QRCODESD" (scontrino digitale, dalla versione 6.0 del vecchio
    /// monitor) e l'immagine in Base64 sulla riga dopo.
    /// </summary>
    Qr,
    /// <summary>Qualunque altro contenuto: messaggio del visore (mai visto finora sul campo).</summary>
    Message,
}

/// <summary>Una riga utile dello scontrino, nell'ordine in cui la cassa la scrive.</summary>
public abstract record ReceiptEntry;

/// <summary>Articolo. <see cref="Amount"/> è l'importo della riga (quantità già compresa).</summary>
public sealed record ItemEntry(string Desc, decimal Qty, long Amount, long? UnitPrice) : ReceiptEntry;

/// <summary>
/// Sconto (negativo) o maggiorazione (positiva). <see cref="UnderItem"/>: allineato a destra sotto un articolo, come lo
/// sconto ripartito sullo scontrino finale; altrimenti riguarda il conto (tra SUBTOT. e TOT. DA PAGARE).
/// </summary>
public sealed record AdjustmentEntry(string Label, long Amount, bool UnderItem) : ReceiptEntry;

/// <summary>Testo che non è nessuna delle righe note: si mostra come nota.</summary>
public sealed record NoteEntry(string Text) : ReceiptEntry;

/// <summary>Righe "PAGATO CONTANTI:  87,00" e "Resto:  0,00": il conto è chiuso.</summary>
public sealed record Payment(string Method, long Amount, long? Change);

public enum QrPurpose
{
    /// <summary>"QRCODE": il cliente paga inquadrandolo (Satispay).</summary>
    Payment,
    /// <summary>"QRCODESD": il cliente scarica lo scontrino digitale, a pagamento fatto.</summary>
    DigitalReceipt,
}

/// <summary>Una versione di Scontrino.txt, già interpretata.</summary>
public sealed class CashFile
{
    public required CashFileKind Kind { get; init; }
    /// <summary>SHA-256 dei byte: due versioni identiche hanno lo stesso hash.</summary>
    public required string Hash { get; init; }
    public string Encoding { get; init; } = "";
    public IReadOnlyList<ReceiptEntry> Entries { get; init; } = [];
    /// <summary>Intestazione del locale (solo in "dettagli conto" e sullo scontrino finale).</summary>
    public IReadOnlyList<string> Header { get; init; } = [];
    public long? Subtotal { get; init; }
    public long? Total { get; init; }
    public Payment? Payment { get; init; }
    /// <summary>
    /// Il conto vero e proprio ("dettagli conto", preconto, scontrino): ha l'intestazione fino alla riga di trattini.
    /// Durante la battuta no. Il vecchio monitor 6.0 nasconde i prezzi finché non c'è (ShowPricesDuringOrder=false).
    /// </summary>
    public bool IsBill { get; init; }
    /// <summary>data:image/png;base64,… per il tipo <see cref="CashFileKind.Qr"/>.</summary>
    public string? QrDataUri { get; init; }
    public QrPurpose QrPurpose { get; init; }
    /// <summary>La riga dopo l'immagine, se non è vuota: il vecchio monitor 6.0 la scrive sopra il QR.</summary>
    public string? QrCaption { get; init; }
    public string? MessageText { get; init; }

    public IEnumerable<ItemEntry> Items => Entries.OfType<ItemEntry>();
}

/// <summary>
/// Interpreta il testo scritto da Passepartout con il modello XML "Monitor" (30 o 48 colonne). Le regole vengono
/// dalle registrazioni in samples/registrazioni: righe articolo con uno spazio davanti e l'importo in fondo, dettaglio
/// quantità prima della riga ("2 X 3,00 Euro", "2 pz X 12,00 Euro"), sconti sul conto o ripartiti sotto le righe,
/// intestazione fino alla riga di trattini e "EURO", "PAGATO …:" e "Resto:" a conto chiuso.
/// </summary>
public static partial class ReceiptParser
{
    [GeneratedRegex(@"^-{10,}$")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^ (?<desc>\S.*?)\s+(?<amount>-?[\d.]+,\d{2})$")]
    private static partial Regex ItemLine();

    // "pz" il 20/10; un'altra unità di misura dell'articolo (kg, bt, lt…) non è ancora stata vista ma costa niente.
    [GeneratedRegex(@"^\s+(?<qty>\d+(?:,\d+)?)\s+(?:[A-Za-z]{1,5}\.?\s+)?X\s+(?<unit>-?[\d.]+,\d{2})\s+Euro$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityLine();

    [GeneratedRegex(@"^\s{2,}(?<label>\S.*?)\s+(?<amount>-?[\d.]+,\d{2})$")]
    private static partial Regex UnderItemAdjustment();

    [GeneratedRegex(@"^(?<label>\S.*?)\s+(?<amount>-?[\d.]+,\d{2})$")]
    private static partial Regex ReceiptAdjustment();

    [GeneratedRegex(@"^PAGATO\s+(?<method>.+?):\s*(?<amount>-?[\d.]+,\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex PaidLine();

    [GeneratedRegex(@"^Resto:\s*(?<amount>-?[\d.]+,\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeLine();

    /// <summary>La valuta in testa alla colonna degli importi, subito dopo i trattini: "EURO" o quella del config.</summary>
    [GeneratedRegex(@"^[A-Za-z€$£]{1,6}$")]
    private static partial Regex CurrencyLine();

    public static CashFile Parse(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var text = TextDecoder.Decode(bytes, out var encoding);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);   // l'a capo finale non è una riga

        if (lines.Count == 0 || lines[0].Trim().Length == 0)
            return new CashFile { Kind = CashFileKind.Empty, Hash = hash, Encoding = encoding };

        if (lines[0].Trim() != "SNF")
        {
            // Come il vecchio monitor: le righe del visore unite da uno spazio.
            var message = string.Join(" ", lines.Select(l => l.Trim()).Where(l => l.Length > 0));
            return new CashFile { Kind = CashFileKind.Message, Hash = hash, Encoding = encoding, MessageText = message };
        }

        var qrIndex = lines.FindIndex(l => l.Trim() is "QRCODE" or "QRCODESD");
        if (qrIndex >= 0)
        {
            var payload = qrIndex + 1 < lines.Count ? lines[qrIndex + 1].Trim() : "";
            var caption = qrIndex + 2 < lines.Count ? lines[qrIndex + 2].Trim() : "";
            var dataUri = ImageDataUri(payload);
            return dataUri == null
                ? new CashFile { Kind = CashFileKind.Empty, Hash = hash, Encoding = encoding }
                : new CashFile
                {
                    Kind = CashFileKind.Qr,
                    Hash = hash,
                    Encoding = encoding,
                    QrDataUri = dataUri,
                    QrPurpose = lines[qrIndex].Trim() == "QRCODESD" ? QrPurpose.DigitalReceipt : QrPurpose.Payment,
                    QrCaption = caption.Length > 0 ? caption : null,
                };
        }

        return ParseReceipt(lines.Skip(1).ToList(), hash, encoding);
    }

    static CashFile ParseReceipt(List<string> lines, string hash, string encoding)
    {
        var header = new List<string>();
        var start = 0;
        var separator = lines.FindIndex(l => Separator().IsMatch(l.Trim()));
        if (separator >= 0)
        {
            header.AddRange(lines.Take(separator).Select(l => l.Trim()).Where(l => l.Length > 0));
            start = separator + 1;
        }

        var entries = new List<ReceiptEntry>();
        var currencyNext = separator >= 0;
        long? subtotal = null, total = null, change = null;
        string? paidMethod = null;
        long paidAmount = 0;
        (decimal Qty, long Unit)? pendingQuantity = null;

        foreach (var raw in lines.Skip(start))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (currencyNext)
            {
                currencyNext = false;
                if (CurrencyLine().IsMatch(trimmed))
                    continue;
            }
            if (trimmed == "EURO")
                continue;

            if (trimmed.StartsWith("SUBTOT", StringComparison.OrdinalIgnoreCase))
            {
                subtotal = LastAmount(trimmed) ?? subtotal;
                continue;
            }
            if (trimmed.StartsWith("TOT.", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("TOTALE", StringComparison.OrdinalIgnoreCase))
            {
                total = LastAmount(trimmed) ?? total;
                continue;
            }

            Match match;
            if ((match = PaidLine().Match(trimmed)).Success && Money.TryParseCents(match.Groups["amount"].Value, out var paid))
            {
                // Pagamento misto (buoni pasto + contanti): il modello XML ha un comando per metodo, quindi una riga
                // per metodo. Si sommano.
                var method = match.Groups["method"].Value.Trim();
                paidMethod = paidMethod == null ? method : $"{paidMethod} + {method}";
                paidAmount += paid;
                continue;
            }
            if ((match = ChangeLine().Match(trimmed)).Success && Money.TryParseCents(match.Groups["amount"].Value, out var rest))
            {
                change = rest;
                continue;
            }
            if ((match = QuantityLine().Match(line)).Success
                && Money.TryParseQuantity(match.Groups["qty"].Value, out var qty)
                && Money.TryParseCents(match.Groups["unit"].Value, out var unit))
            {
                pendingQuantity = (qty, unit);
                continue;
            }
            if ((match = ItemLine().Match(line)).Success && Money.TryParseCents(match.Groups["amount"].Value, out var amount))
            {
                var desc = match.Groups["desc"].Value.Trim();
                if (amount < 0)
                {
                    entries.Add(new AdjustmentEntry(desc, amount, UnderItem: false));
                }
                else
                {
                    // Il dettaglio vale per questa riga solo se quantità × prezzo dà il suo importo (1 cent di
                    // arrotondamento per i pesi): altrimenti era di una riga che non si è riconosciuta.
                    var (q, u) = pendingQuantity is { } p && Math.Abs(Math.Round(p.Qty * p.Unit) - amount) <= 1
                        ? p
                        : (1m, amount);
                    entries.Add(new ItemEntry(desc, q, amount, u));
                }
                pendingQuantity = null;
                continue;
            }
            pendingQuantity = null;   // il dettaglio quantità precede sempre la sua riga articolo
            if ((match = UnderItemAdjustment().Match(line)).Success && Money.TryParseCents(match.Groups["amount"].Value, out var under))
            {
                entries.Add(new AdjustmentEntry(match.Groups["label"].Value.Trim(), under, UnderItem: true));
                continue;
            }
            if ((match = ReceiptAdjustment().Match(line)).Success && Money.TryParseCents(match.Groups["amount"].Value, out var adjustment))
            {
                entries.Add(new AdjustmentEntry(match.Groups["label"].Value.Trim(), adjustment, UnderItem: false));
                continue;
            }
            entries.Add(new NoteEntry(trimmed));
        }

        return new CashFile
        {
            Kind = CashFileKind.Receipt,
            Hash = hash,
            Encoding = encoding,
            Entries = entries,
            Header = header,
            IsBill = separator >= 0,
            Subtotal = subtotal,
            Total = total,
            Payment = paidMethod == null ? null : new Payment(paidMethod, paidAmount, change),
        };
    }

    static long? LastAmount(string line)
    {
        var last = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return last != null && Money.TryParseCents(last, out var cents) ? cents : null;
    }

    /// <summary>
    /// Il QR arriva come immagine in Base64 su una riga; il display la vuole come data URI. Un PNG riconosciuto come QR
    /// viene ridotto a un pixel per modulo (<see cref="QrImage"/>); altrimenti passa com'è.
    /// </summary>
    static string? ImageDataUri(string base64)
    {
        if (base64.Length == 0)
            return null;
        byte[] data;
        try
        {
            data = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
        var type = data switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [0xFF, 0xD8, ..] => "image/jpeg",
            [0x47, 0x49, 0x46, ..] => "image/gif",
            [0x42, 0x4D, ..] => "image/bmp",
            _ => null,
        };
        if (type == null)
            return null;
        if (type == "image/png" && QrImage.ToModules(data) is { } modules)
            return $"data:image/png;base64,{Convert.ToBase64String(modules)}";
        return $"data:{type};base64,{base64}";
    }
}
