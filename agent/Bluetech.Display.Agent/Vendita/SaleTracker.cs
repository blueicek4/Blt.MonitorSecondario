using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;

namespace Bluetech.Display.Agent.Vendita;

/// <summary>
/// Trasforma le versioni di Scontrino.txt in messaggi per il display. Passepartout riscrive sempre il conto intero:
/// qui si confronta il conto letto con le righe già a video e si manda solo quello che cambia, con id stabili.
///
/// Regole, dalle registrazioni in samples/registrazioni:
/// - riga nuova in fondo → <c>sale.line</c> con un id nuovo; quantità o importo cambiati → <c>sale.line</c> con lo
///   stesso id; riga sparita e nessun'altra riga uguale cambiata → storno (<c>void</c> con <c>voidsId</c>);
/// - righe uguali unite in "dettagli conto" ("2 X 3,00 Euro"), sconti spostati sotto le righe, ordine cambiato: non
///   sono storni, si manda lo stato completo (<c>sync</c>);
/// - "PAGATO …" chiude la vendita; il conto successivo ne apre una nuova. Senza PAGATO, un conto che non ha nessuna
///   riga in comune con quello a video è una vendita nuova (il coperto non conta: c'è su ogni tavolo);
/// - un conto dopo il QR toglie il QR, tranne lo stesso conto rimandato entro 20 s (il vecchio monitor 6.0 ignora ogni
///   file per 20 s dopo il QR: la cassa rimanda il conto mentre il cliente paga);
/// - "QRCODESD", il QR dello scontrino digitale, è un messaggio a parte (<c>receipt.qr</c>);
/// - una versione identica alla precedente non produce niente.
///
/// Non è thread-safe: lo usa un solo chiamante alla volta (vedi <see cref="Server.AgentEngine"/>).
/// </summary>
public sealed class SaleTracker
{
    readonly List<SaleLine> _visible = [];
    long _nextId;
    bool _open;
    long _total;
    // L'ultimo totale di un conto non pagato, anche dopo la chiusura per inattività: un QR che arriva dopo è per quello.
    long _lastBillTotal;
    int _cover;
    bool _bill;
    PayQr? _qr;
    DateTimeOffset _qrAt;
    string? _qrHash;
    string? _lastHash;

    public DateTimeOffset LastActivity { get; private set; }
    public bool IsOpen => _open;
    public long Total => _total;
    public string Phase => _qr != null ? "payment" : _open ? "sale" : "idle";
    public IReadOnlyList<SaleLine> Lines => _visible;

    public IReadOnlyList<CashIn> Apply(CashFile file, DateTimeOffset now)
    {
        // Anche una riscrittura identica dice che la cassa sta lavorando su questo conto: il tempo riparte.
        if (file.Kind != CashFileKind.Empty)
            LastActivity = now;
        if (file.Hash == _lastHash)
            return [];
        _lastHash = file.Hash;

        switch (file.Kind)
        {
            case CashFileKind.Message:
                return [new DisplayMessage(file.MessageText ?? "")];
            case CashFileKind.Qr when file.QrPurpose == QrPurpose.DigitalReceipt:
            {
                var messages = new List<CashIn>();
                if (_qr != null)
                {
                    _qr = null;   // lo scontrino digitale arriva a pagamento fatto
                    messages.Add(new PayClear());
                }
                messages.Add(new ReceiptQr(file.QrDataUri!, file.QrCaption));
                return messages;
            }
            case CashFileKind.Qr:
                _qr = new PayQr(file.QrDataUri!, _open ? _total : _lastBillTotal, file.QrCaption);
                _qrAt = now;
                _qrHash = file.Hash;
                return [_qr];
            case CashFileKind.Receipt:
                return ApplyReceipt(file, now);
            default:
                return [];
        }
    }

    /// <summary>Con il QR a video il cliente sta pagando: il QR resta almeno tanto (80 s nella registrazione del 20/10).</summary>
    public static readonly TimeSpan MinimumWithQr = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Lo stesso conto rimandato entro questo tempo dal QR non lo toglie. Il vecchio monitor 6.0 ignora ogni file per
    /// TimeoutClearDisplay (20 s) dopo il QR: segno che la cassa rimanda il conto mentre il cliente paga.
    /// </summary>
    public static readonly TimeSpan QrResendWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Vendita aperta e nessun file da <paramref name="timeout"/>: si chiude e il display torna alle slide, come faceva il
    /// vecchio monitor. Con il QR di pagamento a video si aspetta almeno <see cref="MinimumWithQr"/>.
    /// </summary>
    public IReadOnlyList<CashIn> CheckIdle(DateTimeOffset now, TimeSpan timeout)
    {
        if (_qr != null && timeout > TimeSpan.Zero && timeout < MinimumWithQr)
            timeout = MinimumWithQr;
        if (timeout <= TimeSpan.Zero || now - LastActivity < timeout)
            return [];
        if (!_open)
        {
            // Un QR arrivato senza conto aperto (dopo la chiusura per inattività) scade anche lui.
            if (_qr == null)
                return [];
            _qr = null;
            return [new PayClear()];
        }
        Reset();
        _lastHash = null;   // se la cassa rimanda lo stesso conto (tavolo riaperto), deve tornare a video
        return [new SaleClose(null)];
    }

    /// <summary>Chiude subito la vendita aperta (fine di un replay); il conto successivo, anche uguale, torna a video.</summary>
    public IReadOnlyList<CashIn> CloseNow()
    {
        _lastHash = null;
        if (!_open)
            return [];
        Reset();
        return [new SaleClose(null)];
    }

    /// <summary>Lo stato completo per <c>sync</c> (Partial&lt;DisplayState&gt; del protocollo).</summary>
    public JsonObject Snapshot()
    {
        var state = new JsonObject
        {
            ["phase"] = Phase,
            ["lines"] = new JsonArray(_visible.Select(l => (JsonNode)l.ToJson()).ToArray()),
            ["total"] = _total,
            ["cover"] = _cover,
            ["bill"] = _bill,
        };
        if (_qr != null)
        {
            var payment = new JsonObject { ["provider"] = "satispay", ["png"] = _qr.Png, ["amount"] = _qr.Amount };
            if (_qr.Caption != null)
                payment["caption"] = _qr.Caption;
            state["payment"] = payment;
        }
        return state;
    }

    List<CashIn> ApplyReceipt(CashFile receipt, DateTimeOffset now)
    {
        var messages = new List<CashIn>();
        if (_qr != null)
        {
            if (receipt.Payment == null && now - _qrAt < QrResendWindow && SameBill(receipt))
            {
                _lastHash = _qrHash;   // il QR resta; lo stesso conto più tardi (pagamento annullato) lo toglierà
                return messages;
            }
            messages.Add(new PayClear());
            _qr = null;
        }

        var items = receipt.Items.ToList();
        if (_open && StartsNewSale(items))
        {
            messages.Add(new SaleClose(null));
            Reset();
        }

        var opening = !_open;
        if (opening)
        {
            messages.Add(new SaleOpen());
            _open = true;
        }

        var (lineMessages, needsSync) = Reconcile(receipt.Entries);
        var total = receipt.Total ?? _total;
        var cover = CountCover();
        var totalChanged = opening || total != _total || cover != _cover || receipt.IsBill != _bill;
        _total = total;
        _lastBillTotal = total;
        _cover = cover;
        _bill = receipt.IsBill;

        if (needsSync && !opening)
        {
            messages.Add(new Sync(Snapshot()));
        }
        else
        {
            messages.AddRange(lineMessages);
            if (totalChanged)
                messages.Add(new SaleTotal(_total, _cover, _bill));
        }

        if (receipt.Payment != null)
        {
            messages.Add(new SaleClose(receipt.Payment));
            Reset();
            _lastBillTotal = 0;
        }
        return messages;
    }

    /// <summary>
    /// Nessuna riga in comune con il conto a video. Il coperto non conta: c'è su quasi ogni tavolo, e un tavolo nuovo
    /// che parte dal coperto mostrerebbe come storni i piatti del tavolo prima.
    /// </summary>
    /// <summary>Il conto letto è quello a video, riga per riga, con lo stesso totale.</summary>
    bool SameBill(CashFile receipt) =>
        _open
        && (receipt.Total ?? _total) == _total
        && receipt.Entries.Count == _visible.Count
        && receipt.Entries.Zip(_visible).All(p => Key(p.First) == Key(p.Second)
            && (p.First is not ItemEntry item || (item.Qty == p.Second.Qty && item.Amount == p.Second.Amount)));

    bool StartsNewSale(List<ItemEntry> items)
    {
        var current = _visible.Where(l => l.Kind == LineKind.Item && !IsCover(l.Desc)).Select(l => Key(l.Desc)).ToHashSet();
        return current.Count > 0 && items.Count > 0 && !items.Any(i => current.Contains(Key(i.Desc)));
    }

    static bool IsCover(string desc) => Normalize(desc).StartsWith("COPERT", StringComparison.Ordinal);

    /// <summary>
    /// Allinea le righe a video al conto letto. Restituisce i messaggi incrementali, oppure needsSync se il risultato
    /// dei soli messaggi incrementali non coinciderebbe col conto (righe unite, sconti spostati, ordine diverso).
    /// </summary>
    (List<CashIn> Messages, bool NeedsSync) Reconcile(IReadOnlyList<ReceiptEntry> entries)
    {
        var pools = new Dictionary<string, Queue<SaleLine>>();
        foreach (var line in _visible)
        {
            var key = Key(line);
            if (!pools.TryGetValue(key, out var queue))
                pools[key] = queue = new Queue<SaleLine>();
            queue.Enqueue(line);
        }

        var target = new List<(ReceiptEntry Entry, SaleLine Line, bool IsNew)>();
        foreach (var entry in entries)
        {
            if (pools.TryGetValue(Key(entry), out var queue) && queue.Count > 0)
                target.Add((entry, queue.Dequeue(), false));
            else
                target.Add((entry, NewLine(entry), true));
        }
        var leftovers = pools.Values.SelectMany(q => q).ToHashSet();

        var messages = new List<CashIn>();
        var needsSync = false;

        // Righe sparite: storno solo se le altre righe uguali (stessa descrizione) sono rimaste come erano.
        var changedKeys = target
            .Where(t => !t.IsNew && t.Entry is ItemEntry item && (item.Qty != t.Line.Qty || item.Amount != t.Line.Amount))
            .Select(t => Key(t.Line))
            .ToHashSet();
        foreach (var gone in _visible.Where(leftovers.Contains))
        {
            if (gone.Kind == LineKind.Item && !changedKeys.Contains(Key(gone)))
                messages.Add(new SaleLineMessage(new SaleLine
                {
                    Id = NextId(),
                    Kind = LineKind.Void,
                    Desc = gone.Desc,
                    Amount = gone.Amount,
                    VoidsId = gone.Id,
                }));
            else
                needsSync = true;   // unita a un'altra riga, o uno sconto/una nota che è cambiata
        }

        // Quantità o importo cambiati: stessa riga, stesso id.
        foreach (var (entry, line, isNew) in target)
        {
            if (isNew || entry is not ItemEntry item || (item.Qty == line.Qty && item.Amount == line.Amount))
                continue;
            line.Qty = item.Qty;
            line.Amount = item.Amount;
            messages.Add(new SaleLineMessage(line));
        }

        // Il display aggiunge le righe nuove in fondo: se l'ordine che ne risulta non è quello del conto, sync.
        var incremental = _visible.Where(l => !leftovers.Contains(l)).Concat(target.Where(t => t.IsNew).Select(t => t.Line));
        if (!incremental.Select(l => l.Id).SequenceEqual(target.Select(t => t.Line.Id)))
            needsSync = true;

        messages.AddRange(target.Where(t => t.IsNew).Select(t => new SaleLineMessage(t.Line)));

        _visible.Clear();
        _visible.AddRange(target.Select(t => t.Line));
        return (messages, needsSync);
    }

    SaleLine NewLine(ReceiptEntry entry) => entry switch
    {
        ItemEntry item => new SaleLine { Id = NextId(), Kind = LineKind.Item, Desc = item.Desc, Qty = item.Qty, Amount = item.Amount },
        AdjustmentEntry adj => new SaleLine
        {
            Id = NextId(),
            Kind = adj.Amount < 0 ? LineKind.Discount : LineKind.Surcharge,
            Desc = adj.Label,
            Amount = adj.Amount,
            UnderItem = adj.UnderItem,
        },
        NoteEntry note => new SaleLine { Id = NextId(), Kind = LineKind.Note, Desc = note.Text },
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    string NextId() => "L" + ++_nextId;

    /// <summary>Coperti dalle righe "Coperto"/"Coperti" (il protocollo li vuole a parte).</summary>
    int CountCover() => (int)_visible
        .Where(l => l.Kind == LineKind.Item && IsCover(l.Desc))
        .Sum(l => l.Qty);

    void Reset()
    {
        _open = false;
        _visible.Clear();
        _total = 0;
        _cover = 0;
        _bill = false;
        _qr = null;
    }

    static string Key(ReceiptEntry entry) => entry switch
    {
        ItemEntry item => "I|" + Normalize(item.Desc),
        AdjustmentEntry adj => $"A|{Normalize(adj.Label)}|{adj.Amount}|{adj.UnderItem}",
        NoteEntry note => "N|" + Normalize(note.Text),
        _ => "?",
    };

    static string Key(SaleLine line) => line.Kind switch
    {
        LineKind.Item => "I|" + Normalize(line.Desc),
        LineKind.Discount or LineKind.Surcharge => $"A|{Normalize(line.Desc)}|{line.Amount}|{line.UnderItem}",
        _ => "N|" + Normalize(line.Desc),
    };

    static string Key(string desc) => "I|" + Normalize(desc);

    static string Normalize(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
