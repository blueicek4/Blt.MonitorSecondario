using System.Text.Json.Nodes;

namespace Bluetech.Display.Agent.Vendita;

/// <summary>
/// I messaggi cassa → monitor di display/src/types/protocol.ts, in JSON. Importi in centesimi interi.
/// Le aggiunte, documentate in design/ALLINEAMENTO.md §8:
/// - <c>sale.line</c> con un <c>id</c> già inviato aggiorna quella riga (quantità o importo cambiati);
/// - <c>hb</c>, battito ogni pochi secondi: con una cassa a file il silenzio è normale e non deve far comparire
///   "connessione assente";
/// - <c>sale.close.paid</c>, <c>sale.total.bill</c>, <c>pay.qr.caption</c> e <c>receipt.qr</c> (scontrino digitale).
/// </summary>
public abstract record CashIn
{
    public abstract string Type { get; }

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["t"] = Type };
        Fill(json);
        return json;
    }

    protected virtual void Fill(JsonObject json)
    {
    }

    /// <summary>Sigillato: altrimenti ogni record derivato genererebbe il suo ToString e non uscirebbe JSON.</summary>
    public sealed override string ToString() => ToJson().ToJsonString();
}

public sealed record SaleOpen : CashIn
{
    public override string Type => "sale.open";
}

public sealed record SaleLineMessage(SaleLine Line) : CashIn
{
    public override string Type => "sale.line";
    protected override void Fill(JsonObject json) => json["line"] = Line.ToJson();
}

/// <summary>
/// <see cref="Bill"/> (estensione): la cassa mostra il conto (dettagli conto, preconto, scontrino), non la battuta. Con
/// l'opzione "prezzi solo sul conto" (ShowPricesDuringOrder=false del vecchio monitor) il display nasconde importi e
/// totale finché è falso.
/// </summary>
public sealed record SaleTotal(long Total, int Cover, bool Bill = false) : CashIn
{
    public override string Type => "sale.total";
    protected override void Fill(JsonObject json)
    {
        json["total"] = Total;
        json["cover"] = Cover;
        json["bill"] = Bill;
    }
}

/// <summary>Fine vendita. <see cref="Paid"/> (estensione) c'è quando la cassa ha scritto la riga PAGATO.</summary>
public sealed record SaleClose(Cassa.Payment? Paid) : CashIn
{
    public override string Type => "sale.close";
    protected override void Fill(JsonObject json)
    {
        if (Paid == null)
            return;
        var paid = new JsonObject { ["method"] = Paid.Method, ["amount"] = Paid.Amount };
        if (Paid.Change.HasValue)
            paid["change"] = Paid.Change.Value;
        json["paid"] = paid;
    }
}

public sealed record DisplayMessage(string Text) : CashIn
{
    public override string Type => "display.msg";
    protected override void Fill(JsonObject json) => json["text"] = Text;
}

/// <summary><see cref="Caption"/> (estensione): la didascalia che la cassa scrive dopo l'immagine, se c'è.</summary>
public sealed record PayQr(string Png, long Amount, string? Caption = null) : CashIn
{
    public override string Type => "pay.qr";
    protected override void Fill(JsonObject json)
    {
        json["provider"] = "satispay";
        json["png"] = Png;
        json["amount"] = Amount;
        if (Caption != null)
            json["caption"] = Caption;
    }
}

/// <summary>
/// Estensione: il QR per scaricare lo scontrino digitale ("QRCODESD", vecchio monitor 6.0). Arriva a pagamento fatto:
/// il display lo mostra nello stato Grazie al posto del QR di ringraziamento del pannello.
/// </summary>
public sealed record ReceiptQr(string Png, string? Caption) : CashIn
{
    public override string Type => "receipt.qr";
    protected override void Fill(JsonObject json)
    {
        json["png"] = Png;
        if (Caption != null)
            json["caption"] = Caption;
    }
}

public sealed record PayClear : CashIn
{
    public override string Type => "pay.clear";
}

/// <summary>Stato completo: alla connessione del display e quando le righe cambiano in modo non incrementale.</summary>
public sealed record Sync(JsonObject State) : CashIn
{
    public override string Type => "sync";
    protected override void Fill(JsonObject json) => json["state"] = State.DeepClone();
}

public sealed record Heartbeat : CashIn
{
    public override string Type => "hb";
}

public enum LineKind
{
    Item,
    Discount,
    Surcharge,
    Void,
    Note,
}

/// <summary>Una riga del monitor, con l'id stabile per tutta la vendita.</summary>
public sealed class SaleLine
{
    public required string Id { get; init; }
    public required LineKind Kind { get; init; }
    public string Desc { get; set; } = "";
    public decimal Qty { get; set; } = 1;
    public long Amount { get; set; }
    public string? VoidsId { get; init; }
    /// <summary>Per gli sconti: sotto un articolo (ripartito) o sul conto.</summary>
    public bool UnderItem { get; init; }

    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["kind"] = Kind switch
            {
                LineKind.Item => "item",
                LineKind.Discount => "discount",
                LineKind.Surcharge => "surcharge",
                LineKind.Void => "void",
                _ => "note",
            },
            ["id"] = Id,
        };
        switch (Kind)
        {
            case LineKind.Note:
                json["text"] = Desc;
                break;
            case LineKind.Item:
                json["desc"] = Desc;
                json["qty"] = Qty;
                json["amount"] = Amount;
                break;
            case LineKind.Void:
                json["desc"] = Desc;
                json["amount"] = Amount;
                if (VoidsId != null)
                    json["voidsId"] = VoidsId;
                break;
            default:
                json["desc"] = Desc;
                json["amount"] = Amount;
                break;
        }
        return json;
    }
}
