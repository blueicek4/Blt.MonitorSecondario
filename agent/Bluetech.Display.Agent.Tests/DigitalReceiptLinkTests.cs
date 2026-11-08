using System.Text;
using Bluetech.Display.Agent.Cassa;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>
/// Lo scontrino digitale come lo scrive Passepartout (registrazione del 08/11, anonimizzata): il QR in PNG con il link
/// intatto e, 0,3 s dopo, lo scontrino pagato con "QRCODE" e il link storpiato dal modello XML (/ → -, ? → spazio).
/// </summary>
public class DigitalReceiptLinkTests
{
    const string Recording = "2026-11-08_173725";
    const string Link54 = "https://scontrinodigitalecloud.passepartout.cloud/v1/download?token=00000000-0000-4000-8000-000000000054";
    const string Mangled54 = "https:--scontrinodigitalecloud.passepartout.cloud-v1-download token=00000000-0000-4000-8000-000000000054";

    [Theory]
    [InlineData(Mangled54)]   // come arriva il 08/11
    [InlineData(Link54)]      // intatto (un modello XML che non lo tocca)
    [InlineData("https:--scontrinodigitalecloud.passepartout.cloud-v1-downloadtoken=00000000-0000-4000-8000-000000000054")]   // "?" tolto
    [InlineData("https://scontrinodigitalecloud.passepartout.cloud/v1/download token=00000000-0000-4000-8000-000000000054")]   // solo "?" cambiato
    [InlineData("  HTTPS:--ScontrinoDigitaleCloud.Passepartout.Cloud-v1-download token=00000000-0000-4000-8000-000000000054  ")]
    public void The_link_comes_back_from_what_the_file_says(string text) =>
        Assert.Equal(Link54, DigitalReceiptLink.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S6Y-PAY--00000000-0000-4000-8000-000000000035")]                                      // Satispay
    [InlineData("https:--www.example.com-v1-download token=00000000-0000-4000-8000-000000000054")]       // un altro sito
    [InlineData("https:--passepartout.cloud.example.com-v1-download token=00000000-0000-4000-8000-000000000054")]
    [InlineData("https:--scontrinodigitalecloud.passepartout.cloud-v1-download token=non-un-token")]
    [InlineData("https:--scontrinodigitalecloud.passepartout.cloud-v1-download token=00000000-0000-4000-8000-000000000054 e altro")]
    [InlineData("https:--example.com-lascia-una-recensione")]                                            // un QR per altro
    public void Anything_else_is_not_a_digital_receipt(string? text) => Assert.Null(DigitalReceiptLink.Parse(text));

    [Fact]
    public void A_very_long_line_is_not_even_looked_at() =>
        Assert.Null(DigitalReceiptLink.Parse(Mangled54 + new string(' ', 600) + "x"));

    [Theory]
    [InlineData("Scarica il tuo scontrino digitale!", true)]
    [InlineData("SCARICA LO SCONTRINO DIGITALE", true)]
    [InlineData("Paga con Satispay", false)]
    [InlineData(null, false)]
    public void The_caption_says_digital_receipt(string? caption, bool expected) =>
        Assert.Equal(expected, DigitalReceiptLink.IsCaption(caption));

    [Fact]
    public void The_qr_image_before_the_payment_is_the_digital_receipt_and_its_link_is_read()
    {
        var file = ReceiptParser.Parse(Samples.Version(Recording, "0054"));
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.Equal(QrPurpose.DigitalReceipt, file.QrPurpose);   // "QRCODE", non "QRCODESD": la didascalia e il link
        Assert.Equal("Scarica il tuo scontrino digitale!", file.QrCaption);
        Assert.Equal(Link54, file.QrText);
        Assert.Equal(Link54, file.DigitalReceiptUrl);
    }

    [Fact]
    public void The_satispay_qr_is_read_and_stays_a_payment()
    {
        var file = ReceiptParser.Parse(Samples.Version(Recording, "0035"));
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.Equal(QrPurpose.Payment, file.QrPurpose);
        Assert.Equal("S6Y-PAY--00000000-0000-4000-8000-000000000035", file.QrText);
        Assert.Null(file.DigitalReceiptUrl);
        Assert.Null(file.QrCaption);
    }

    [Fact]
    public void The_fake_qr_of_20_october_cannot_be_read_and_stays_a_payment()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008"));
        Assert.Equal(QrPurpose.Payment, file.QrPurpose);
        Assert.Null(file.QrText);
    }

    [Fact]
    public void The_paid_receipt_with_the_link_at_the_bottom_is_a_receipt_with_its_link()
    {
        var file = ReceiptParser.Parse(Samples.Version(Recording, "0055"));
        Assert.Equal(CashFileKind.Receipt, file.Kind);   // prima: vuoto, e il PAGATO si perdeva
        Assert.True(file.IsBill);
        Assert.Equal("BAR ESEMPIO", file.Header[0]);
        Assert.Equal([new ItemEntry("Verdure Fritte", 1, 10, 10)], file.Entries);   // QRCODE e il link non sono righe
        Assert.Equal(10, file.Total);
        Assert.Equal(new Payment("CONTANTI", 10, 0), file.Payment);
        Assert.Equal(Mangled54, file.QrText);
        Assert.Equal(Link54, file.DigitalReceiptUrl);
    }

    [Fact]
    public void Another_qr_printed_on_the_receipt_is_dropped_and_the_receipt_stays()
    {
        var file = Parse("SNF", " CAFFE                     1,20", "TOT. DA PAGARE            1,20", "PAGATO CONTANTI:  1,20", " ",
            "QRCODE", "https:--www.example.com-lascia-una-recensione", " ", "Grazie e arrivederci");
        Assert.Equal(CashFileKind.Receipt, file.Kind);
        Assert.Equal(["CAFFE"], file.Items.Select(i => i.Desc));
        Assert.Equal([new NoteEntry("Grazie e arrivederci")], file.Entries.OfType<NoteEntry>());   // il piede resta
        Assert.NotNull(file.Payment);
        Assert.Equal("https:--www.example.com-lascia-una-recensione", file.QrText);
        Assert.Null(file.DigitalReceiptUrl);
    }

    [Fact]
    public void Only_the_link_without_the_receipt_makes_the_qr_here()
    {
        var file = Parse("SNF", " ", "QRCODE", Mangled54, " ");
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.Equal(QrPurpose.DigitalReceipt, file.QrPurpose);
        Assert.Equal(Link54, file.DigitalReceiptUrl);
        Assert.Equal(Link54, QrImage.ReadText(FromDataUri(file.QrDataUri!)));   // il QR fatto qui porta il link vero

        Assert.Equal(CashFileKind.Empty, Parse("SNF", " ", "QRCODE", "testo qualunque", " ").Kind);   // come prima
    }

    [Fact]
    public void A_voucher_line_with_its_percentage_is_a_note_not_an_item()
    {
        // " BUONO welcome20 Sconto 20,00" seguito da spazi fino a 48 colonne: lo sconto del 20% (15,50 → -3,10).
        var file = ReceiptParser.Parse(Samples.Version(Recording, "0051"));
        Assert.Equal(["Birra Birrina Bionda 75Cl", "Birra Birrina Bionda Ipa 33Cl"], file.Items.Select(i => i.Desc));
        Assert.Contains(new NoteEntry("BUONO welcome20 Sconto 20,00"), file.Entries);
        Assert.Contains(new AdjustmentEntry("SCONTO", -310, UnderItem: false), file.Entries);
        Assert.Equal(1240, file.Total);
    }

    [Theory]
    [InlineData("S6Y-PAY--00000000-0000-4000-8000-000000000035")]
    [InlineData(Link54)]
    [InlineData("Caffè à la carte €")]
    public void A_qr_made_here_reads_back(string text) =>
        Assert.Equal(text, QrImage.ReadText(FromDataUri(QrImage.FromText(text))));

    [Fact]
    public void Something_that_is_not_a_qr_reads_as_nothing()
    {
        Assert.Null(QrImage.ReadText([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]));
        Assert.Null(QrImage.ReadText([]));
        var blank = new bool[21 * 21];
        Assert.Null(QrImage.ReadText(QrImage.Encode(blank, 21)));
    }

    static CashFile Parse(params string[] lines) =>
        ReceiptParser.Parse(Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n"));

    static byte[] FromDataUri(string uri) => Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);
}

public class DigitalReceiptStatisticsTests
{
    [Fact]
    public void The_same_digital_receipt_sent_again_with_its_bill_counts_once()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bt-stat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var stats = new Server.Statistics(folder);
            var first = new Vendita.DigitalReceipt { Id = "R1-AAAA", Png = "data:image/png;base64,AA==" };
            var again = new Vendita.DigitalReceipt { Id = "R1-AAAA", Png = "data:image/png;base64,AA==" };
            var other = new Vendita.DigitalReceipt { Id = "R2-BBBB", Png = "data:image/png;base64,AA==" };
            stats.Count("cassa1", [new Vendita.ReceiptDigital(first)]);
            stats.Count("cassa1", [new Vendita.SaleClose(new Payment("CONTANTI", 10, 0)), new Vendita.ReceiptDigital(again)]);
            stats.Count("cassa1", [new Vendita.ReceiptDigital(other)]);
            var today = stats.Report(1)[0]!;
            Assert.Equal(2, (long)FindCounter(today, "ScontriniDigitali"));
            Assert.Equal(1, (long)FindCounter(today, "Pagate"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    static long FindCounter(System.Text.Json.Nodes.JsonNode day, string name)
    {
        // Il rapporto del back office: un giorno con le sue casse; qui una sola.
        var json = day.ToJsonString();
        var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + name + "\":(\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(match.Success, json);
        return long.Parse(match.Groups[1].Value);
    }
}
