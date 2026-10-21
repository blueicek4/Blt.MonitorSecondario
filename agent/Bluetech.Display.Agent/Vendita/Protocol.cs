using System.Text.Json.Nodes;

namespace Bluetech.Display.Agent.Vendita;

/// <summary>
/// I messaggi cassa → monitor di display/src/types/protocol.ts, in JSON. Importi in centesimi interi.
/// Le aggiunte, documentate in design/ALLINEAMENTO.md §8:
/// - <c>sale.line</c> con un <c>id</c> già inviato aggiorna quella riga (quantità o importo cambiati);
/// - <c>hb</c>, battito ogni pochi secondi: con una cassa a file il silenzio è normale e non deve far comparire
///   "connessione assente";
/// - <c>sale.close.paid</c>, <c>sale.total.bill</c>, <c>pay.qr.caption</c> e <c>receipt.qr</c> (scontrino digitale);
/// - <c>receipt.digital</c>, <c>receipt.clear</c> e <c>receipt.paper.status</c> (scontrino digitale a video e copia
///   cartacea, docs/03); dal display all'Agent solo <c>receipt.paper</c>.
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

/// <summary>
/// Estensione (docs/03): lo scontrino digitale da mostrare, con il conto pagato se l'abbinamento è sicuro e il pulsante
/// della copia cartacea. Lo stesso <c>id</c> rimandato aggiorna lo stato della copia.
/// </summary>
public sealed record ReceiptDigital(DigitalReceipt Receipt) : CashIn
{
    public override string Type => "receipt.digital";
    protected override void Fill(JsonObject json)
    {
        foreach (var (key, value) in Receipt.ToJson())
            json[key] = value?.DeepClone();
    }
}

/// <summary>Estensione: lo scontrino digitale non è più a video (tempo scaduto o vendita nuova).</summary>
public sealed record ReceiptClear(string Id) : CashIn
{
    public override string Type => "receipt.clear";
    protected override void Fill(JsonObject json) => json["id"] = Id;
}

/// <summary>
/// Estensione: com'è andata la richiesta della copia cartacea. <c>requested</c> = la copia è partita verso la
/// stampante; <c>already</c> = l'aveva già chiesta qualcuno; <c>failed</c> = da chiedere alla cassa.
/// </summary>
public sealed record PaperStatus(string Id, string State, string? Message = null) : CashIn
{
    public override string Type => "receipt.paper.status";
    protected override void Fill(JsonObject json)
    {
        json["id"] = Id;
        json["state"] = State;
        if (Message != null)
            json["message"] = Message;
    }
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

/// <summary>
/// Estensione (ALLINEAMENTO §8): la disposizione forzata dal back office per questo display, solo a lui, al
/// collegamento e quando cambia. <see cref="Force"/> null = quella che il display sceglie dal rapporto d'aspetto.
/// </summary>
public sealed record LayoutMessage(string? Force) : CashIn
{
    public override string Type => "layout";
    protected override void Fill(JsonObject json) => json["force"] = Force;

    /// <summary>Dai nomi del back office a quelli del design: larga, stretta, verticale, palmare.</summary>
    public static LayoutMessage For(string? disposizione) => new(disposizione switch
    {
        "larga" => "wide",
        "stretta" => "narrow",
        "verticale" => "portrait",
        "palmare" => "handheld",
        _ => null,
    });
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
