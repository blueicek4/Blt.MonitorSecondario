using System.Text;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Vendita;

namespace Bluetech.Display.Agent.Stampa;

public enum CopyAlign
{
    Left,
    Center,
    Right,
}

public abstract record CopyBlock;

/// <summary>Una riga già impaginata. <see cref="Large"/>: doppia larghezza e altezza, quindi metà delle colonne.</summary>
public sealed record CopyText(string Text, CopyAlign Align = CopyAlign.Left, bool Bold = false, bool Large = false) : CopyBlock;

/// <summary>
/// Il QR come immagine. <see cref="Modules"/>: un elemento per modulo (il QR ridotto dall'Agent); falso quando
/// l'immagine non si è potuta ridurre e ogni elemento è un pixel.
/// </summary>
public sealed record CopyQr(bool[] Dark, int Width, int Height, bool Modules) : CopyBlock;

/// <summary>
/// La copia cartacea dello scontrino digitale (docs/03 §5), nell'ordine di uno scontrino: intestazione, righe, sconti,
/// totale, pagamenti, data, il QR piccolo e la dicitura. Tutto viene dal conto pagato già passato dal file del monitor:
/// Passepartout non c'entra. Senza conto abbinato resta il solo QR, che è la parte che vale ai fini fiscali.
/// </summary>
public static class PaperCopy
{
    public static IReadOnlyList<CopyBlock> Compose(DigitalReceipt receipt, PaperCopyOptions options)
    {
        var cols = Math.Clamp(options.Colonne, 24, 64);
        var blocks = new List<CopyBlock>();
        var bill = receipt.Bill;
        if (bill != null)
        {
            foreach (var line in bill.Header.Where(h => h.Length > 0))
                blocks.Add(new CopyText(Cut(line, cols), CopyAlign.Center));
            blocks.Add(new CopyText(new string('-', cols)));
            blocks.Add(new CopyText("EURO", CopyAlign.Right));
            foreach (var entry in bill.Entries)
                AddEntry(blocks, entry, cols);
            blocks.Add(new CopyText(new string('-', cols)));
            if (bill.Total is { } total)
                blocks.Add(new CopyText(Pair("TOTALE", Money.Format(total), cols / 2), Bold: true, Large: true));
            blocks.Add(new CopyText(""));
            if (bill.Payment is { } paid)
            {
                AddPair(blocks, "Pagato " + paid.Method, Money.Format(paid.Amount), cols);
                if (paid.Change is { } change)
                    blocks.Add(new CopyText(Pair("Resto", Money.Format(change), cols)));
            }
        }
        else
        {
            blocks.Add(new CopyText(Cut(receipt.Caption ?? "Scontrino digitale", cols), CopyAlign.Center, Bold: true));
        }

        blocks.Add(new CopyText(""));
        blocks.Add(new CopyText(receipt.IssuedAt.ToString("dd/MM/yyyy HH:mm")));
        blocks.Add(new CopyText(""));
        if (Qr(receipt.Png) is { } qr)
            blocks.Add(qr);
        foreach (var line in Wrap(options.Dicitura, cols))
            blocks.Add(new CopyText(line, CopyAlign.Center));
        return blocks;
    }

    /// <summary>
    /// La copia a video, nel blocco "carta" dello scontrino digitale: le stesse righe della copia stampata fino alla data,
    /// senza il QR piccolo e la dicitura (a video il QR è il blocco accanto). <c>Large</c>: gli indici delle righe a
    /// doppia altezza (il totale), già impaginate su metà delle colonne.
    /// </summary>
    public static (List<string> Lines, List<int> Large) ScreenLines(DigitalReceipt receipt, int columns)
    {
        var cols = Math.Clamp(columns, 24, 64);
        var lines = new List<string>();
        var large = new List<int>();
        foreach (var block in Compose(receipt, new PaperCopyOptions { Colonne = cols, Dicitura = "" }))
        {
            if (block is not CopyText text)
                break;   // il QR: da qui in giù la copia è per la carta
            if (text.Large)
                large.Add(lines.Count);
            lines.Add(Place(text.Text, text.Align, text.Large ? cols / 2 : cols).TrimEnd());
        }
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return (lines, large);
    }

    /// <summary>La copia come testo semplice: per la modalità prova e per i test.</summary>
    public static string ToText(IReadOnlyList<CopyBlock> blocks, int columns)
    {
        var cols = Math.Clamp(columns, 24, 64);
        var text = new StringBuilder();
        foreach (var block in blocks)
        {
            var line = block switch
            {
                CopyText t => Place(t.Text, t.Align, t.Large ? cols / 2 : cols),
                CopyQr q => Place($"[QR {q.Width}x{q.Height}]", CopyAlign.Center, cols),
                _ => "",
            };
            text.Append(line.TrimEnd()).Append('\n');
        }
        return text.ToString();
    }

    static void AddEntry(List<CopyBlock> blocks, ReceiptEntry entry, int cols)
    {
        switch (entry)
        {
            case ItemEntry item:
                if (item.Qty != 1)
                {
                    var unit = item.UnitPrice is { } price ? " x " + Money.Format(price) : "";
                    blocks.Add(new CopyText($"  {Money.FormatQuantity(item.Qty)}{unit}"));
                }
                blocks.Add(new CopyText(Pair(item.Desc, Money.Format(item.Amount), cols)));
                break;
            case AdjustmentEntry adj:
                blocks.Add(new CopyText(Pair(adj.UnderItem ? "    " + adj.Label : adj.Label, Money.Format(adj.Amount), cols)));
                break;
            case NoteEntry note:
                foreach (var line in Wrap(note.Text, cols))
                    blocks.Add(new CopyText(line));
                break;
        }
    }

    /// <summary>Etichetta e importo sulla stessa riga; se l'etichetta è troppo lunga va su più righe.</summary>
    static void AddPair(List<CopyBlock> blocks, string label, string amount, int cols)
    {
        if (label.Length + amount.Length + 1 <= cols)
        {
            blocks.Add(new CopyText(Pair(label, amount, cols)));
            return;
        }
        var lines = Wrap(label, cols);
        foreach (var line in lines.Take(lines.Count - 1))
            blocks.Add(new CopyText(line));
        blocks.Add(new CopyText(Pair(lines[^1], amount, cols)));
    }

    /// <summary>"Caffe'" a sinistra e "1,40" a destra, sulla larghezza data; l'etichetta si taglia se serve.</summary>
    static string Pair(string label, string amount, int width)
    {
        var room = Math.Max(1, width - amount.Length - 1);
        var left = Cut(label, room);
        return left + new string(' ', Math.Max(1, width - left.Length - amount.Length)) + amount;
    }

    static string Place(string text, CopyAlign align, int width)
    {
        text = Cut(text, width);
        return align switch
        {
            CopyAlign.Center => new string(' ', (width - text.Length) / 2) + text,
            CopyAlign.Right => text.PadLeft(width),
            _ => text,
        };
    }

    static string Cut(string text, int width) => text.Length <= width ? text : text[..width];

    static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0)
                current.Append(' ');
            current.Append(Cut(word, width));
        }
        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current.ToString());
        return lines;
    }

    /// <summary>Il QR dal data URI: ridotto a un pixel per modulo dall'Agent, oppure l'immagine così com'è.</summary>
    static CopyQr? Qr(string dataUri)
    {
        var comma = dataUri.IndexOf(',');
        if (comma < 0)
            return null;
        byte[] png;
        try
        {
            png = Convert.FromBase64String(dataUri[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
        var image = QrImage.Decode(png);
        if (image == null)
            return null;
        // Il QR ridotto ha al massimo 177 moduli per lato (versione 40); un'immagine più grande è in pixel.
        return new CopyQr(image.Dark, image.Width, image.Height, Modules: image.Width <= 177 && image.Width == image.Height);
    }
}
