using System.Text;
using System.Text.Json.Nodes;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Vendita;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// Le registrazioni vere passate dal tracker a un "display di prova" che applica i messaggi come farà quello vero:
/// dopo ogni versione quello che il display mostra deve coincidere con il conto della cassa.
/// </summary>
public class SaleTrackerTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(2));

    /// <summary>Il display di prova: le regole di protocol.ts più le due aggiunte (sale.line = aggiorna, hb).</summary>
    sealed class DisplayModel
    {
        public readonly List<JsonObject> Lines = [];
        public bool Open;
        public long Total;
        public JsonObject? Payment;
        public readonly List<JsonObject> Received = [];

        public void Apply(IEnumerable<CashIn> messages)
        {
            foreach (var message in messages)
            {
                var json = JsonNode.Parse(message.ToString())!.AsObject();
                Received.Add(json);
                switch ((string)json["t"]!)
                {
                    case "sale.open":
                        Open = true;
                        Lines.Clear();
                        Total = 0;
                        break;
                    case "sale.line":
                        var line = json["line"]!.AsObject();
                        var index = Lines.FindIndex(l => (string)l["id"]! == (string)line["id"]!);
                        if (index >= 0)
                            Lines[index] = line;
                        else
                            Lines.Add(line);
                        Open = true;
                        break;
                    case "sale.total":
                        Total = (long)json["total"]!;
                        break;
                    case "sale.close":
                        Open = false;
                        Lines.Clear();
                        Total = 0;
                        Payment = null;
                        break;
                    case "pay.qr":
                        Payment = json;
                        break;
                    case "pay.clear":
                        Payment = null;
                        break;
                    case "sync":
                        var state = json["state"]!.AsObject();
                        Lines.Clear();
                        Lines.AddRange(state["lines"]!.AsArray().Select(n => n!.AsObject()));
                        Total = (long)state["total"]!;
                        Open = (string)state["phase"]! != "idle";
                        break;
                }
            }
        }

        /// <summary>Le righe che il cliente vede: senza storni e senza le righe stornate.</summary>
        public List<string> Visible()
        {
            var voided = Lines.Where(l => (string)l["kind"]! == "void").Select(l => (string?)l["voidsId"]).ToHashSet();
            return Lines
                .Where(l => (string)l["kind"]! != "void" && !voided.Contains((string)l["id"]!))
                .Select(l => (string)l["kind"]! switch
                {
                    "item" => $"{l["desc"]} ×{(decimal)l["qty"]!} {l["amount"]}",
                    "note" => $"nota {l["text"]}",
                    _ => $"{l["kind"]} {l["desc"]} {l["amount"]}",
                })
                .ToList();
        }
    }

    static List<string> Expected(CashFile file) => file.Entries.Select(e => e switch
    {
        ItemEntry item => $"{item.Desc} ×{item.Qty} {item.Amount}",
        AdjustmentEntry adj => $"{(adj.Amount < 0 ? "discount" : "surcharge")} {adj.Label} {adj.Amount}",
        NoteEntry note => $"nota {note.Text}",
        _ => "?",
    }).ToList();

    static (SaleTracker Tracker, DisplayModel Model, Dictionary<string, List<CashIn>> ByVersion) Replay(string recording)
    {
        var tracker = new SaleTracker();
        var model = new DisplayModel();
        var byVersion = new Dictionary<string, List<CashIn>>();
        var now = T0;
        foreach (var (name, bytes) in Samples.Versions(recording))
        {
            now = now.AddSeconds(1);
            var file = ReceiptParser.Parse(bytes);
            var messages = tracker.Apply(file, now).ToList();
            byVersion[name[..4]] = messages;
            model.Apply(messages);

            if (file.Kind == CashFileKind.Receipt && file.Payment == null)
            {
                Assert.True(model.Open, $"{recording} #{name[..4]}: vendita chiusa");
                Assert.Equal(Expected(file), model.Visible());
                Assert.True(file.Total == model.Total, $"{recording} #{name[..4]}: totale {model.Total} invece di {file.Total}");
                Assert.Null(model.Payment);
            }
            else if (file.Kind == CashFileKind.Receipt)
            {
                Assert.False(model.Open, $"{recording} #{name[..4]}: pagato ma ancora aperto");
            }
            else if (file.Kind == CashFileKind.Qr)
            {
                Assert.NotNull(model.Payment);
            }
        }
        return (tracker, model, byVersion);
    }

    static IEnumerable<JsonObject> Json(IEnumerable<CashIn> messages) => messages.Select(m => m.ToJson());

    static bool IsLine(JsonObject json, string kind) => (string)json["t"]! == "sale.line" && (string)json["line"]!["kind"]! == kind;

    [Theory]
    [InlineData("2026-10-20_002138")]
    [InlineData("2026-10-20_064854")]
    [InlineData("2026-10-20_075632")]
    public void Every_version_of_a_real_recording_shows_exactly_the_bill(string recording) => Replay(recording);

    [Fact]
    public void Night_of_20_october_merges_voids_quantities_and_payments()
    {
        var (_, model, v) = Replay("2026-10-20_002138");

        // #07 "dettagli conto": le due righe uguali diventano una, con ×2. Non è uno storno.
        Assert.DoesNotContain(Json(v["0007"]), m => IsLine(m, "void"));
        Assert.Contains(Json(v["0007"]), m => (string)m["t"]! == "sync");

        // #32: Gramigna tolta e un articolo nuovo PRIMA della riga "SCONTO" del conto: aggiunto in fondo finirebbe
        // dopo lo sconto, quindi stato completo (sync) senza Gramigna.
        var sync32 = Json(v["0032"]).Single();
        Assert.Equal("sync", (string)sync32["t"]!);
        var lines32 = sync32["state"]!["lines"]!.AsArray().Select(l => l!.AsObject()).ToList();
        Assert.DoesNotContain(lines32, l => (string?)l["desc"] == "Gramigna alla salsiccia");
        Assert.Equal("discount", (string)lines32[^1]["kind"]!);

        // #33: Strozzapreti tolta = storno; Tortellini in brodo ×2 = la stessa riga, aggiornata.
        Assert.Contains(Json(v["0033"]), m => IsLine(m, "void") && (string)m["line"]!["desc"]! == "Strozzapreti con verdurine");
        var update = Json(v["0033"]).Single(m => IsLine(m, "item"));
        Assert.Equal("Tortellini in brodo", (string)update["line"]!["desc"]!);
        Assert.Equal(2m, (decimal)update["line"]!["qty"]!);
        Assert.Equal((string)lines32.Single(l => (string?)l["desc"] == "Tortellini in brodo")["id"]!, (string)update["line"]!["id"]!);

        // Quattro conti pagati; il terzo, senza riga di pagamento, si chiude quando arriva il quarto (#24).
        var closes = v.Values.SelectMany(Json).Where(m => (string)m["t"]! == "sale.close").ToList();
        Assert.Equal(4, closes.Count(c => c["paid"] is not null));
        Assert.Contains(Json(v["0024"]), m => (string)m["t"]! == "sale.close" && m["paid"] is null);
        Assert.Equal(["CONTANTI", "C.CREDITO", "CONTANTI", "CONTANTI"], closes.Where(c => c["paid"] is not null).Select(c => (string)c["paid"]!["method"]!));
        Assert.Equal(6, v.Values.SelectMany(Json).Count(m => (string)m["t"]! == "sale.open"));
        Assert.True(model.Open);   // il sesto conto resta aperto
    }

    [Fact]
    public void Identical_versions_send_nothing()
    {
        var (_, _, v) = Replay("2026-10-20_002138");
        foreach (var same in new[] { "0008", "0015", "0022", "0023", "0035", "0041", "0042" })
            Assert.Empty(v[same]);
    }

    [Fact]
    public void Ab_test_void_and_paid_with_change()
    {
        var (_, _, v) = Replay("2026-10-20_064854");
        Assert.Contains(Json(v["0017"]), m => IsLine(m, "void") && (string)m["line"]!["desc"]! == "Tagliata di Manzo");
        Assert.Contains(Json(v["0027"]), m => (string)m["t"]! == "sync");
        var close = Json(v["0028"]).Single(m => (string)m["t"]! == "sale.close");
        Assert.Equal(45000, (long)close["paid"]!["amount"]!);
        Assert.Equal(11050, (long)close["paid"]!["change"]!);
    }

    [Fact]
    public void Qr_shows_with_the_last_total_and_goes_away_with_the_next_bill()
    {
        var (_, _, v) = Replay("2026-10-20_075632");
        var qr = Json(v["0008"]).Single();
        Assert.Equal("pay.qr", (string)qr["t"]!);
        Assert.Equal(1030, (long)qr["amount"]!);
        Assert.StartsWith("data:image/png;base64,", (string)qr["png"]!);
        Assert.Equal("pay.clear", (string)Json(v["0009"]).First()["t"]!);
    }

    [Fact]
    public void Ringing_up_sends_only_the_new_line_and_the_total()
    {
        var (_, _, v) = Replay("2026-10-20_075632");
        Assert.Equal(["sale.line", "sale.total"], Json(v["0003"]).Select(m => (string)m["t"]!));
    }

    [Fact]
    public void A_receipt_paid_straight_away_opens_and_closes_with_payment()
    {
        // Lo scontrino del bar mostrato sul vecchio monitor: caffè, sconto sotto la riga, carta, resto 0.
        var model = new DisplayModel();
        var messages = new SaleTracker().Apply(ReceiptParser.Parse(Samples.Receipt("scontrino-bar-pagato-carta.txt")), T0);
        Assert.Equal(["sale.open", "sale.line", "sale.line", "sale.total", "sale.close"], messages.Select(m => m.Type));
        model.Apply(messages.Take(4));
        Assert.Equal(["Caffe' ×1 140", "discount SCONTO -20"], model.Visible());
        Assert.Equal(120, model.Total);
        var paid = messages[^1].ToJson()["paid"]!;
        Assert.Equal("C.CREDITO", (string)paid["method"]!);
        Assert.Equal(120, (long)paid["amount"]!);
        Assert.Equal(0, (long)paid["change"]!);
    }

    static CashFile Bill(params string[] items)
    {
        var lines = new List<string> { "SNF", " " };
        long total = 0;
        foreach (var item in items)
        {
            lines.Add($" {item,-22}{"1,00",7}");
            total += 100;
        }
        lines.Add($"TOT. DA PAGARE{$"{total / 100},{total % 100:00}",16}");
        return ReceiptParser.Parse(Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n"));
    }

    [Fact]
    public void An_abandoned_bill_closes_after_the_timeout_and_comes_back_if_resent()
    {
        var tracker = new SaleTracker();
        var bill = Bill("CAFFE");
        Assert.NotEmpty(tracker.Apply(bill, T0));
        Assert.Empty(tracker.CheckIdle(T0.AddSeconds(299), TimeSpan.FromSeconds(300)));
        Assert.Equal("sale.close", Assert.Single(tracker.CheckIdle(T0.AddSeconds(300), TimeSpan.FromSeconds(300))).Type);
        Assert.Contains(tracker.Apply(bill, T0.AddSeconds(301)), m => m.Type == "sale.open");
    }

    [Fact]
    public void The_payment_qr_stays_longer_than_the_idle_timeout()
    {
        var tracker = new SaleTracker();
        var timeout = TimeSpan.FromSeconds(60);
        tracker.Apply(ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0007")), T0);
        tracker.Apply(ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008")), T0.AddSeconds(5));
        Assert.Equal("payment", tracker.Phase);
        Assert.Empty(tracker.CheckIdle(T0.AddSeconds(5 + 90), timeout));   // 80 s il 20/10, prima del PAGATO
        Assert.Equal("sale.close", Assert.Single(tracker.CheckIdle(T0.AddSeconds(5) + SaleTracker.MinimumWithQr, timeout)).Type);
    }

    [Fact]
    public void A_bill_with_nothing_in_common_is_a_new_sale()
    {
        var tracker = new SaleTracker();
        tracker.Apply(Bill("CAFFE", "BRIOCHE"), T0);
        var next = tracker.Apply(Bill("SPRITZ"), T0.AddSeconds(5));
        Assert.Equal(["sale.close", "sale.open", "sale.line", "sale.total"], next.Select(m => m.Type));
    }

    [Fact]
    public void A_new_table_that_shares_only_the_cover_is_a_new_sale()
    {
        // Tavolo 5 non pagato (nessuna riga PAGATO, come il terzo conto del 20/10), poi il tavolo 7 parte dal coperto.
        var tracker = new SaleTracker();
        tracker.Apply(Bill("Coperto", "Tagliatelle", "Acqua"), T0);
        var next = tracker.Apply(Bill("Coperto"), T0.AddSeconds(40));
        Assert.Equal(["sale.close", "sale.open", "sale.line", "sale.total"], next.Select(m => m.Type));
        Assert.DoesNotContain(next, m => m is SaleLineMessage { Line.Kind: LineKind.Void });

        // Lo stesso tavolo che toglie un piatto resta la stessa vendita.
        var same = tracker.Apply(Bill("Coperto", "Lasagne"), T0.AddSeconds(50));
        Assert.DoesNotContain(same, m => m.Type == "sale.open");
    }

    [Fact]
    public void A_qr_after_the_idle_close_shows_the_last_bill_and_then_expires()
    {
        var tracker = new SaleTracker();
        var timeout = TimeSpan.FromSeconds(60);
        tracker.Apply(ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0007")), T0);
        Assert.Equal("sale.close", Assert.Single(tracker.CheckIdle(T0.AddSeconds(60), timeout)).Type);

        var qr = Assert.IsType<PayQr>(Assert.Single(tracker.Apply(ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008")), T0.AddSeconds(70))));
        Assert.Equal(1030, qr.Amount);   // il conto chiuso per inattività, non 0
        Assert.Empty(tracker.CheckIdle(T0.AddSeconds(70) + SaleTracker.MinimumWithQr - TimeSpan.FromSeconds(1), timeout));
        Assert.Equal("pay.clear", Assert.Single(tracker.CheckIdle(T0.AddSeconds(70) + SaleTracker.MinimumWithQr, timeout)).Type);
        Assert.Equal("idle", tracker.Phase);
    }

    [Fact]
    public void An_identical_rewrite_restarts_the_idle_time()
    {
        var tracker = new SaleTracker();
        var timeout = TimeSpan.FromSeconds(60);
        tracker.Apply(Bill("CAFFE"), T0);
        Assert.Empty(tracker.Apply(Bill("CAFFE"), T0.AddSeconds(50)));   // stesso conto: niente al display…
        Assert.Empty(tracker.CheckIdle(T0.AddSeconds(100), timeout));      // …ma la cassa ci sta lavorando
        Assert.Single(tracker.CheckIdle(T0.AddSeconds(110), timeout));
    }

    [Fact]
    public void Snapshot_for_a_display_that_connects_mid_sale()
    {
        var tracker = new SaleTracker();
        tracker.Apply(Bill("CAFFE", "BRIOCHE"), T0);
        var state = tracker.Snapshot();
        Assert.Equal("sale", (string)state["phase"]!);
        Assert.Equal(2, state["lines"]!.AsArray().Count);
        Assert.Equal(200, (long)state["total"]!);
    }
}
