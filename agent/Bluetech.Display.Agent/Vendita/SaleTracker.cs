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
/// - il QR dello scontrino digitale ("QRCODESD", o "QRCODE" con la didascalia o il link dello scontrino) è un messaggio a
///   parte: <c>receipt.qr</c>, oppure, con lo scontrino digitale attivo (docs/03), <c>receipt.digital</c> con il conto
///   pagato abbinato, se l'abbinamento è sicuro. Passepartout (08/11) scrive il QR a conto aperto e 0,3 s dopo lo
///   scontrino pagato con il link in fondo: il QR aspetta quel PAGATO (<see cref="EarlyQrWait"/>) e va con lui. Se arriva
///   solo il link (in fondo allo scontrino pagato), il QR si fa qui; se arrivano tutti e due, vale l'immagine della cassa;
/// - con il conto a video (dettagli conto, preconto) la vendita si chiude per inattività dopo almeno
///   <see cref="MinimumWithBill"/>: durante il pagamento con il POS o con la cassa automatica la cassa non scrive niente;
/// - una versione identica alla precedente non produce niente;
/// - in modalità demo, un conto pagato senza QR dello scontrino digitale entro <see cref="DemoDelay"/> riceve un QR finto
///   (<see cref="DigitalReceiptSettings.DemoQr"/>): si prova lo scontrino digitale e la copia di carta senza il servizio vero.
///
/// Non è thread-safe: lo usa un solo chiamante alla volta (vedi <see cref="Server.AgentEngine"/>). Uno per cassa.
/// </summary>
public sealed class SaleTracker(DigitalReceiptSettings? digital = null)
{
    /// <summary>
    /// Il QR dello scontrino digitale si abbina all'ultimo conto pagato solo se arriva entro questo tempo e nel frattempo
    /// non è partita un'altra vendita. Altrimenti resta il solo QR: meglio niente righe che le righe di un altro cliente.
    /// </summary>
    public static readonly TimeSpan PairWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Modalità demo: quanto si aspetta dopo PAGATO il QR vero prima di fare quello finto. Il tempo che la cassa vera ci
    /// mette a mandarlo, e il Grazie ha finito la sua celebrazione.
    /// </summary>
    public static readonly TimeSpan DemoDelay = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Il QR dello scontrino digitale arrivato a vendita aperta aspetta lo scontrino pagato che lo segue (0,3 s il 08/11,
    /// due volte su due). Se non arriva, il QR va a video da solo, senza conto abbinato.
    /// </summary>
    public static readonly TimeSpan EarlyQrWait = TimeSpan.FromSeconds(3);

    DigitalReceiptSettings _digital = digital ?? DigitalReceiptSettings.Off;
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
    // L'ultimo conto pagato, con l'ora del tracker e l'ora vera: il QR dello scontrino digitale che arriva dopo è suo.
    (CashFile Bill, DateTimeOffset At, DateTimeOffset Wall)? _lastPaid;
    DigitalReceipt? _receipt;
    long _nextReceipt;
    // Modalità demo: il conto pagato che aspetta il suo QR finto.
    (CashFile Bill, DateTimeOffset At, DateTimeOffset Wall)? _demoPending;
    // Il QR dello scontrino digitale arrivato prima del PAGATO, che lo aspetta (EarlyQrWait).
    (CashFile Qr, DateTimeOffset At, DateTimeOffset Wall)? _earlyQr;
    // L'ultimo scontrino digitale andato a video con il suo link: la stessa cosa ripetuta (l'immagine dopo il testo, il
    // PAGATO dopo il QR già a video da solo) non ne fa un altro.
    (string Url, DateTimeOffset At, string Png, string? Caption)? _lastLink;

    public DateTimeOffset LastActivity { get; private set; }
    public bool IsOpen => _open;
    public long Total => _total;
    public string Phase => _qr != null ? "payment" : _open ? "sale" : _receipt != null ? "receipt" : "idle";
    public IReadOnlyList<SaleLine> Lines => _visible;
    /// <summary>Lo scontrino digitale a video, se c'è.</summary>
    public DigitalReceipt? Receipt => _receipt;

    /// <summary>Cambiato dal back office: vale dal prossimo QR; lo scontrino già a video resta com'è.</summary>
    public DigitalReceiptSettings Digital
    {
        get => _digital;
        set => _digital = value ?? DigitalReceiptSettings.Off;
    }

    /// <param name="wallNow">L'ora vera, per la data sulla copia; senza, quella di <paramref name="now"/>.</param>
    public IReadOnlyList<CashIn> Apply(CashFile file, DateTimeOffset now, DateTimeOffset? wallNow = null)
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
                _demoPending = null;   // è arrivato quello vero
                if (Repeated(file.DigitalReceiptUrl, now))
                    return [];   // già a video, fatto dal link in fondo allo scontrino pagato
                if (_open)
                {
                    // A vendita aperta (08/11): aspetta lo scontrino pagato, che arriva subito dopo, per andare con lui dopo
                    // il Grazie. Intanto il display resta com'è, anche con il QR di Satispay.
                    _earlyQr = (file, now, wallNow ?? now);
                    return [];
                }
                var messages = new List<CashIn>();
                if (_qr != null)
                {
                    _qr = null;   // lo scontrino digitale arriva a pagamento fatto
                    messages.Add(new PayClear());
                }
                var paid = _lastPaid is { } last && now - last.At <= PairWindow ? last : ((CashFile, DateTimeOffset, DateTimeOffset)?)null;
                messages.AddRange(ShowReceipt(file.QrDataUri!, file.QrCaption, file.DigitalReceiptUrl, paid, now, wallNow ?? now));
                return messages;
            }
            case CashFileKind.Qr:
                _qr = new PayQr(file.QrDataUri!, _open ? _total : _lastBillTotal, file.QrCaption);
                _qrAt = now;
                _qrHash = file.Hash;
                return [_qr];
            case CashFileKind.Receipt:
                return ApplyReceipt(file, now, wallNow ?? now);
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
        if (_earlyQr is { } early && now - early.At >= EarlyQrWait)
        {
            // Il PAGATO non è arrivato: il QR va a video da solo. Il conto a video (dettagli conto, preconto) era quello
            // pagato e si chiude; una battuta in corso no, è il cliente dopo.
            _earlyQr = null;
            var messages = new List<CashIn>();
            if (_open && _bill)
            {
                Reset();
                messages.Add(new SaleClose(null));
            }
            else if (_qr != null)
            {
                _qr = null;
                messages.Add(new PayClear());
            }
            messages.AddRange(ShowReceipt(early.Qr.QrDataUri!, early.Qr.QrCaption, early.Qr.DigitalReceiptUrl, null, now, early.Wall));
            return [.. messages, .. CheckSaleIdle(now, timeout)];
        }
        if (_demoPending is { } pending && now - pending.At >= DemoDelay)
        {
            _demoPending = null;
            if (_digital.DemoQr is { } demoQr && !_open && _qr == null)
            {
                var id = NextReceiptId();
                _receipt = new DigitalReceipt
                {
                    Id = id,
                    Png = demoQr(id),
                    Caption = "Scontrino di prova",
                    Bill = pending.Bill,
                    ShownAt = now,
                    IssuedAt = pending.Wall,
                    PaperCopy = _digital.PaperCopy,
                    Notice = _digital.Notice,
                    Demo = true,
                };
                return [new ReceiptDigital(_receipt), .. CheckSaleIdle(now, timeout)];
            }
        }
        if (_receipt != null && now - _receipt.ShownAt >= _digital.OnScreen)
        {
            var expired = _receipt;
            _receipt = null;
            return [new ReceiptClear(expired.Id), .. CheckSaleIdle(now, timeout)];
        }
        return CheckSaleIdle(now, timeout);
    }

    string NextReceiptId() => "R" + ++_nextReceipt + "-" + Convert.ToHexString(Guid.NewGuid().ToByteArray(), 0, 4);

    /// <summary>
    /// Lo scontrino digitale a video: con la schermata dedicata (<c>receipt.digital</c>, il conto pagato se l'abbinamento
    /// è sicuro), altrimenti il QR nel Grazie (<c>receipt.qr</c>).
    /// </summary>
    List<CashIn> ShowReceipt(string png, string? caption, string? url, (CashFile Bill, DateTimeOffset At, DateTimeOffset Wall)? paid, DateTimeOffset now, DateTimeOffset wallNow)
    {
        if (url != null)
            _lastLink = (url, now, png, caption);
        if (!_digital.Show)
            return [new ReceiptQr(png, caption)];
        _receipt = new DigitalReceipt
        {
            Id = NextReceiptId(),
            Png = png,
            Caption = caption,
            Url = url,
            Bill = paid?.Bill,
            ShownAt = now,
            IssuedAt = paid?.Wall ?? wallNow,
            PaperCopy = _digital.PaperCopy,
            Notice = _digital.Notice,
        };
        return [new ReceiptDigital(_receipt)];
    }

    /// <summary>
    /// Lo scontrino pagato che arriva quando il suo QR (stesso link) è già a video da solo, perché oltre
    /// <see cref="EarlyQrWait"/>: è lo stesso acquisto. Il Grazie, poi lo stesso scontrino digitale (stesso id) ora con il
    /// conto; senza la schermata dedicata, il Grazie e di nuovo il QR. Null = un conto qualunque.
    /// </summary>
    List<CashIn>? LatePayment(CashFile receipt, DateTimeOffset now, DateTimeOffset wallNow)
    {
        if (_open || receipt.Payment == null || receipt.DigitalReceiptUrl is not { } url || !Repeated(url, now))
            return null;
        _lastPaid = (receipt, now, wallNow);
        _demoPending = null;
        if (_receipt is { } shown && shown.Url == url)
        {
            if (shown.Bill != null)
                return [];   // già abbinato: lo stesso scontrino rimandato
            _receipt = new DigitalReceipt
            {
                Id = shown.Id,
                Png = shown.Png,
                Caption = shown.Caption,
                Url = shown.Url,
                Bill = receipt,
                ShownAt = shown.ShownAt,
                IssuedAt = wallNow,
                PaperCopy = shown.PaperCopy,
                Notice = shown.Notice,
                Paper = shown.Paper,
            };
            return [new SaleClose(receipt.Payment), new ReceiptDigital(_receipt)];
        }
        if (_digital.Show)
            return null;   // lo scontrino a video è già scaduto o tolto: il conto va come sempre
        var last = _lastLink!.Value;
        return [new SaleClose(receipt.Payment), new ReceiptQr(last.Png, last.Caption)];
    }

    /// <summary>Lo stesso link di scontrino digitale andato a video da poco: è lo stesso scontrino, mandato in due modi.</summary>
    bool Repeated(string? url, DateTimeOffset now) =>
        url != null && _lastLink is { } last && last.Url == url && now - last.At <= PairWindow;

    /// <summary>La copia cartacea dello scontrino digitale <paramref name="id"/> cambia stato; falso se non è più a video.</summary>
    public bool SetPaper(string id, PaperState state)
    {
        if (_receipt?.Id != id)
            return false;
        _receipt.Paper = state;
        return true;
    }

    /// <summary>
    /// Con il conto a video (dettagli conto, preconto) il cliente sta per pagare, e mentre paga con il POS o con la cassa
    /// automatica Passepartout non scrive niente (08/11): la vendita resta almeno tanto, anche se la chiusura è più breve.
    /// </summary>
    public static readonly TimeSpan MinimumWithBill = TimeSpan.FromMinutes(3);

    IReadOnlyList<CashIn> CheckSaleIdle(DateTimeOffset now, TimeSpan timeout)
    {
        if (_qr != null && timeout > TimeSpan.Zero && timeout < MinimumWithQr)
            timeout = MinimumWithQr;
        else if (_open && _bill && timeout > TimeSpan.Zero && timeout < MinimumWithBill)
            timeout = MinimumWithBill;
        if (timeout <= TimeSpan.Zero || now - LastActivity < timeout)
            return [];
        if (!_open)
        {
            // Un QR arrivato senza conto aperto (dopo la chiusura per inattività) scade anche lui.
            if (_qr == null)
                return [];
            _qr = null;
            _lastHash = null;   // lo stesso QR rimandato dopo deve tornare a video
            return [new PayClear(), new SaleClose(null)];   // senza conto: il display torna all'attesa
        }
        Reset();
        _lastHash = null;   // se la cassa rimanda lo stesso conto (tavolo riaperto), deve tornare a video
        return [new SaleClose(null)];
    }

    /// <summary>Chiude subito la vendita aperta (fine di un replay); il conto successivo, anche uguale, torna a video.</summary>
    public IReadOnlyList<CashIn> CloseNow()
    {
        _lastHash = null;
        _demoPending = null;
        _earlyQr = null;
        var messages = new List<CashIn>();
        if (_receipt != null)
        {
            messages.Add(new ReceiptClear(_receipt.Id));
            _receipt = null;
        }
        if (_open)
        {
            Reset();
            messages.Add(new SaleClose(null));
        }
        return messages;
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
        if (_receipt != null)
            state["receipt"] = _receipt.ToJson();   // chi si ricollega (un palmare uscito dallo standby) lo rivede
        return state;
    }

    List<CashIn> ApplyReceipt(CashFile receipt, DateTimeOffset now, DateTimeOffset wallNow)
    {
        if (LatePayment(receipt, now, wallNow) is { } late)
            return late;
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
            _earlyQr = null;   // era della vendita di prima, che non è arrivata al PAGATO
        }

        var opening = !_open;
        if (opening)
        {
            // Il cliente dopo: lo scontrino digitale del precedente non resta a video, e un QR che arrivasse adesso
            // non è più di quel conto.
            if (_receipt != null)
            {
                messages.Add(new ReceiptClear(_receipt.Id));
                _receipt = null;
            }
            _lastPaid = null;
            _demoPending = null;
            _earlyQr = null;
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
            _lastPaid = (receipt, now, wallNow);
            // Il QR arrivato poco prima va con questo conto, se i link (quando si conoscono tutti e due) sono lo stesso.
            var early = _earlyQr is { } e && now - e.At <= PairWindow
                && (e.Qr.DigitalReceiptUrl == null || receipt.DigitalReceiptUrl == null || e.Qr.DigitalReceiptUrl == receipt.DigitalReceiptUrl)
                ? e.Qr
                : null;
            _earlyQr = null;
            if (early != null)
                messages.AddRange(ShowReceipt(early.QrDataUri!, early.QrCaption, early.DigitalReceiptUrl ?? receipt.DigitalReceiptUrl, _lastPaid, now, wallNow));
            else if (receipt.DigitalReceiptUrl is { } url && !Repeated(url, now))
                messages.AddRange(ShowReceipt(QrImage.FromText(url), null, url, _lastPaid, now, wallNow));
            else if (_digital.DemoQr != null && _digital.Show)
                _demoPending = _lastPaid;
        }
        return messages;
    }

    /// <summary>Il conto letto è quello a video, riga per riga, con lo stesso totale.</summary>
    bool SameBill(CashFile receipt) =>
        _open
        && (receipt.Total ?? _total) == _total
        && receipt.Entries.Count == _visible.Count
        && receipt.Entries.Zip(_visible).All(p => Key(p.First) == Key(p.Second)
            && (p.First is not ItemEntry item || (item.Qty == p.Second.Qty && item.Amount == p.Second.Amount)));

    /// <summary>
    /// Nessun piatto in comune con il conto a video: il coperto non conta, c'è su quasi ogni tavolo e un tavolo nuovo che
    /// parte dal coperto mostrerebbe come storni i piatti del tavolo prima. Unica eccezione: restano solo i coperti, uguali,
    /// ed è sparito un piatto solo. È lo stesso tavolo che storna l'ultimo piatto (uno storno toglie una riga per volta).
    /// </summary>
    bool StartsNewSale(List<ItemEntry> items)
    {
        var dishes = _visible.Where(l => l.Kind == LineKind.Item && !IsCover(l.Desc)).ToList();
        var dishKeys = dishes.Select(l => Key(l.Desc)).ToHashSet();
        if (dishKeys.Count == 0 || items.Count == 0 || items.Any(i => dishKeys.Contains(Key(i.Desc))))
            return false;
        var covers = _visible.Where(l => l.Kind == LineKind.Item && IsCover(l.Desc)).Select(l => (Key(l.Desc), l.Qty, l.Amount)).ToHashSet();
        var lastDishVoided = dishes.Count == 1 && items.All(i => IsCover(i.Desc) && covers.Contains((Key(i.Desc), i.Qty, i.Amount)));
        return !lastDishVoided;
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
