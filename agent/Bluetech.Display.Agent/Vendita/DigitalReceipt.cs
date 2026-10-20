using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;

namespace Bluetech.Display.Agent.Vendita;

/// <summary>Cosa fa il tracker con il QR dello scontrino digitale (docs/03).</summary>
/// <param name="Show">Schermata dello scontrino digitale; falso = il <c>receipt.qr</c> di sempre.</param>
/// <param name="PaperCopy">Il pulsante della copia cartacea (c'è una stampante configurata).</param>
/// <param name="OnScreen">Quanto resta a video, se non parte prima un'altra vendita.</param>
/// <param name="Notice">La dicitura della copia, per mostrarla anche a video sotto il QR piccolo.</param>
public sealed record DigitalReceiptSettings(bool Show, bool PaperCopy, TimeSpan OnScreen, string? Notice = null)
{
    public static readonly DigitalReceiptSettings Off = new(false, false, TimeSpan.Zero);
}

public enum PaperState
{
    None,
    Printing,
    /// <summary>La copia è partita verso la stampante: non vuol dire "stampata".</summary>
    Requested,
    Failed,
}

/// <summary>
/// Lo scontrino digitale a video: il QR della cassa e, se l'abbinamento è sicuro, il conto pagato da cui viene. Senza
/// conto resta il solo QR, che è la parte che vale ai fini fiscali (il PDF firmato del documento).
/// </summary>
public sealed class DigitalReceipt
{
    public required string Id { get; init; }
    /// <summary>data:image/png;base64,… del QR, un pixel per modulo quando l'Agent ha potuto ridurlo.</summary>
    public required string Png { get; init; }
    public string? Caption { get; init; }
    /// <summary>Il conto con PAGATO abbinato al QR; null = solo QR.</summary>
    public CashFile? Bill { get; init; }
    /// <summary>Quando è arrivato il QR, nell'orologio del tracker.</summary>
    public DateTimeOffset ShownAt { get; init; }
    /// <summary>Ora vera del pagamento (o del QR, senza conto), per la copia e per lo scontrino a video.</summary>
    public DateTimeOffset IssuedAt { get; set; }
    public bool PaperCopy { get; init; }
    public string? Notice { get; init; }
    public PaperState Paper { get; set; }

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["id"] = Id,
            ["png"] = Png,
            ["paperCopy"] = PaperCopy,
            ["paper"] = Paper switch
            {
                PaperState.Printing => "printing",
                PaperState.Requested => "requested",
                PaperState.Failed => "failed",
                _ => "none",
            },
            ["issuedAt"] = IssuedAt.ToString("O"),
        };
        if (Caption != null)
            json["caption"] = Caption;
        if (Notice != null)
            json["notice"] = Notice;
        if (Bill != null)
            json["receipt"] = BillJson(Bill);
        return json;
    }

    static JsonObject BillJson(CashFile bill)
    {
        var lines = new JsonArray();
        foreach (var entry in bill.Entries)
        {
            lines.Add(entry switch
            {
                ItemEntry item => new JsonObject
                {
                    ["kind"] = "item",
                    ["desc"] = item.Desc,
                    ["qty"] = item.Qty,
                    ["amount"] = item.Amount,
                    ["unit"] = item.UnitPrice,
                },
                AdjustmentEntry adj => new JsonObject
                {
                    ["kind"] = adj.Amount < 0 ? "discount" : "surcharge",
                    ["desc"] = adj.Label,
                    ["amount"] = adj.Amount,
                    ["underItem"] = adj.UnderItem,
                },
                NoteEntry note => new JsonObject { ["kind"] = "note", ["text"] = note.Text },
                _ => new JsonObject { ["kind"] = "note", ["text"] = "" },
            });
        }
        var json = new JsonObject
        {
            ["header"] = new JsonArray(bill.Header.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()),
            ["lines"] = lines,
            ["total"] = bill.Total,
        };
        if (bill.Payment != null)
        {
            var paid = new JsonObject { ["method"] = bill.Payment.Method, ["amount"] = bill.Payment.Amount };
            if (bill.Payment.Change.HasValue)
                paid["change"] = bill.Payment.Change.Value;
            json["paid"] = paid;
        }
        return json;
    }
}
