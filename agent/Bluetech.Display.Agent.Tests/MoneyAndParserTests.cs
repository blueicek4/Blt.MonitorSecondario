using System.Text;
using Bluetech.Display.Agent.Cassa;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

public class MoneyAndParserTests
{
    [Theory]
    [InlineData("1,60", 160)]
    [InlineData("12,00", 1200)]
    [InlineData("450,00", 45000)]
    [InlineData("1.234,50", 123450)]
    [InlineData("-0,41", -41)]
    [InlineData("-5,00", -500)]
    public void Amounts_are_integer_cents(string text, long cents)
    {
        Assert.True(Money.TryParseCents(text, out var value));
        Assert.Equal(cents, value);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("12.00")]
    [InlineData("abc")]
    [InlineData("1.23,00")]
    public void Not_amounts(string text) => Assert.False(Money.TryParseCents(text, out _));

    [Fact]
    public void In_every_real_version_the_total_is_the_sum_of_the_lines()
    {
        var checkedFiles = 0;
        foreach (var recording in Samples.Recordings)
        {
            foreach (var (name, bytes) in Samples.Versions(recording))
            {
                var file = ReceiptParser.Parse(bytes);
                if (file.Kind != CashFileKind.Receipt)
                    continue;
                var sum = file.Entries.Sum(e => e switch
                {
                    ItemEntry item => item.Amount,
                    AdjustmentEntry adj => adj.Amount,
                    _ => 0,
                });
                Assert.True(file.Total.HasValue, $"{recording}/{name}: manca il totale");
                Assert.True(sum == file.Total, $"{recording}/{name}: righe {sum}, totale {file.Total}");
                Assert.DoesNotContain(file.Entries, e => e is NoteEntry);   // nessuna riga non riconosciuta
                checkedFiles++;
            }
        }
        Assert.True(checkedFiles > 80, "versioni controllate: " + checkedFiles);
    }

    [Fact]
    public void Details_with_header_and_merged_quantity()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0007"));
        Assert.Contains("Da inserire", file.Header);
        Assert.Contains("P.I.00000000000", file.Header);
        Assert.Contains(new ItemEntry("Cestino di tigelle 4 pezzi", 2, 600, 300), file.Items);
        Assert.Contains(new ItemEntry("Tagliere Emilia x 2", 1, 3000, 3000), file.Items);   // " x 2" è nel nome
        Assert.Equal(8700, file.Total);
        Assert.Null(file.Payment);
    }

    [Fact]
    public void Quantity_changed_while_ringing_up()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0033"));
        Assert.Contains(new ItemEntry("Tortellini in brodo", 2, 2400, 1200), file.Items);
        Assert.Contains(new AdjustmentEntry("SCONTO", -500, UnderItem: false), file.Entries);
        Assert.Equal(4300, file.Total);
    }

    [Fact]
    public void Discount_spread_under_the_lines_and_payment()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0038"));
        Assert.Equal(3, file.Entries.OfType<AdjustmentEntry>().Count(a => a.UnderItem));
        Assert.Equal(new Payment("CONTANTI", 4300, 0), file.Payment);
        Assert.Equal(4300, file.Total);
    }

    [Fact]
    public void Satispay_qr_becomes_a_data_uri()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008"));
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.StartsWith("data:image/png;base64,iVBORw0KGgo", file.QrDataUri);
    }

    [Fact]
    public void Card_payment_with_change()
    {
        var file = ReceiptParser.Parse(Samples.Version("2026-10-20_064854", "0028"));
        Assert.Equal(new Payment("CONTANTI", 45000, 11050), file.Payment);
        Assert.Equal(33950, file.Total);
        Assert.Equal(new Payment("C.CREDITO", 6500, 0), ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0016")).Payment);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("windows-1252")]
    public void Accents_in_either_encoding(string encodingName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = "SNF\r\n \r\n Tiramisù della casa          5,00\r\n Caffè                         1,20\r\nSUBTOT.                       6,20\r\n \r\nTOT. DA PAGARE                6,20\r\n";
        var file = ReceiptParser.Parse(Encoding.GetEncoding(encodingName).GetBytes(text));
        Assert.Equal(["Tiramisù della casa", "Caffè"], file.Items.Select(i => i.Desc));
        Assert.Equal(620, file.Total);
    }

    [Fact]
    public void Empty_and_display_message()
    {
        Assert.Equal(CashFileKind.Empty, ReceiptParser.Parse([]).Kind);
        var message = ReceiptParser.Parse(Encoding.ASCII.GetBytes("TOTALE\r\n   5,80  \r\n"));
        Assert.Equal(CashFileKind.Message, message.Kind);
        Assert.Equal("TOTALE 5,80", message.MessageText);
    }

    static CashFile Receipt(params string[] lines) =>
        ReceiptParser.Parse(Encoding.ASCII.GetBytes(string.Join("\r\n", ["SNF", " ", .. lines]) + "\r\n"));

    [Fact]
    public void Mixed_payment_adds_up_the_lines()
    {
        // Il modello XML ha un comando per metodo (StampaPagamentoBuonoPasto, …Contanti): una riga ciascuno.
        var file = Receipt(" Menu pranzo             17,50", "TOT. DA PAGARE          17,50", "PAGATO BUONI PASTO:   8,00", "PAGATO CONTANTI:     10,00", "Resto:               0,50");
        Assert.Equal(new Payment("BUONI PASTO + CONTANTI", 1800, 50), file.Payment);
    }

    [Fact]
    public void Quantity_line_with_another_unit_of_measure()
    {
        var file = Receipt("   0,450 kg X 60,00 Euro", " Branzino al sale        27,00", "   2 bt X 18,00 Euro", " Lambrusco               36,00");
        Assert.Equal([new ItemEntry("Branzino al sale", 0.450m, 2700, 6000), new ItemEntry("Lambrusco", 2, 3600, 1800)], file.Items);
        Assert.Empty(file.Entries.OfType<NoteEntry>());
    }

    [Fact]
    public void A_quantity_goes_only_to_the_line_it_adds_up_to()
    {
        // Una riga non riconosciuta (importo attaccato alla descrizione) non passa la sua quantità alla successiva.
        var file = Receipt("  25 X 40,00 Euro", " Menu Comunione Bambini1000,00", " CAPPUCCINO               1,80");
        Assert.Equal(new ItemEntry("CAPPUCCINO", 1, 180, 180), file.Items.Single());
        Assert.Equal(new ItemEntry("Tagliere", 1, 900, 900), Receipt("   2 X 3,00 Euro", " Tagliere                 9,00").Items.Single());
    }

    [Fact]
    public void Utf16_with_bom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("SNF\r\n \r\n Caffè          1,20\r\nTOT. DA PAGARE 1,20\r\n")).ToArray();
        var file = ReceiptParser.Parse(bytes);
        Assert.Equal("UTF-16 LE con BOM", file.Encoding);
        Assert.Equal("Caffè", file.Items.Single().Desc);
    }

    static byte[] QrPng(byte[] bytes)
    {
        var lines = Encoding.ASCII.GetString(bytes).Split("\r\n");
        return Convert.FromBase64String(lines[Array.FindIndex(lines, l => l.Trim() == "QRCODE") + 1].Trim());
    }

    static byte[] FromDataUri(string uri) => Convert.FromBase64String(uri[(uri.IndexOf(',') + 1)..]);

    [Theory]
    [InlineData("qr-di-prova-geometria-satispay.txt")]   // un QR leggibile con la geometria di quello vero
    [InlineData("registrazioni/2026-10-20_075632/0008")]   // il QR finto della registrazione
    public void The_satispay_qr_becomes_one_pixel_per_module(string sample)
    {
        var bytes = sample.StartsWith("registrazioni/") ? Samples.Version("2026-10-20_075632", "0008") : Samples.Receipt(sample);
        var original = QrImage.Decode(QrPng(bytes))!;
        Assert.Equal(197, original.Width);

        var file = ReceiptParser.Parse(bytes);
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.StartsWith("data:image/png;base64,", file.QrDataUri);
        var reduced = QrImage.Decode(FromDataUri(file.QrDataUri!))!;
        Assert.Equal((49, 49), (reduced.Width, reduced.Height));

        // Ogni modulo è uno dei blocchi 4×4 dell'originale, tutto dello stesso colore.
        for (var y = 0; y < 196; y++)
        for (var x = 0; x < 196; x++)
            Assert.Equal(reduced.Dark[y / 4 * 49 + x / 4], original.Dark[y * 197 + x]);

        Assert.Equal(FromDataUri(file.QrDataUri!), QrImage.ToModules(FromDataUri(file.QrDataUri!)));   // già ridotto: uguale
    }

    [Fact]
    public void An_image_that_is_not_a_readable_qr_passes_as_it_is()
    {
        var notQr = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4]);
        var file = ReceiptParser.Parse(Encoding.ASCII.GetBytes($"SNF\r\n \r\nQRCODE\r\n{notQr}\r\n \r\n"));
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.Equal("data:image/png;base64," + notQr, file.QrDataUri);
    }

    // Dal vecchio monitor 6.0 (docs/01 §10).

    [Fact]
    public void The_sample_of_the_6_0_monitor_is_a_bill_with_covers()
    {
        var file = ReceiptParser.Parse(Samples.Receipt("scontrino-monitor-6.0-esempio.txt"));
        Assert.True(file.IsBill);
        Assert.Equal(
            [new ItemEntry("TARTARE DI MANZO", 1, 1800, 1800), new ItemEntry("TARTARE DI MANZO", 1, 1800, 1800), new ItemEntry("Coperti", 2, 400, 200)],
            file.Items);
        Assert.Equal(4000, file.Total);
        Assert.Empty(file.Entries.OfType<NoteEntry>());
    }

    [Fact]
    public void Only_the_bill_has_the_header()
    {
        Assert.False(ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0006")).IsBill);   // battuta
        Assert.True(ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0007")).IsBill);    // dettagli conto
        Assert.True(ReceiptParser.Parse(Samples.Version("2026-10-20_002138", "0009")).IsBill);    // scontrino
    }

    [Fact]
    public void Digital_receipt_qr_with_its_caption()
    {
        var png = QrPng(Samples.Receipt("qr-di-prova-geometria-satispay.txt"));
        var text = $"SNF\r\n \r\nQRCODESD\r\n{Convert.ToBase64String(png)}\r\nScarica il tuo scontrino digitale!\r\n";
        var file = ReceiptParser.Parse(Encoding.ASCII.GetBytes(text));
        Assert.Equal(CashFileKind.Qr, file.Kind);
        Assert.Equal(QrPurpose.DigitalReceipt, file.QrPurpose);
        Assert.Equal("Scarica il tuo scontrino digitale!", file.QrCaption);

        var satispay = ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008"));
        Assert.Equal(QrPurpose.Payment, satispay.QrPurpose);
        Assert.Null(satispay.QrCaption);   // la riga dopo l'immagine è di spazi
    }

    [Fact]
    public void The_currency_line_after_the_dashes_is_skipped_whatever_it_is()
    {
        var file = Receipt("INTESTAZIONE", "------------------------------", "                           EUR", " CAFFE                     1,20", "TOT. DA PAGARE            1,20");
        Assert.Equal(["CAFFE"], file.Items.Select(i => i.Desc));
        Assert.Empty(file.Entries.OfType<NoteEntry>());

        var note = Receipt("INTESTAZIONE", "------------------------------", "                          EURO", "ASPORTO", " CAFFE                     1,20");
        Assert.Equal(new NoteEntry("ASPORTO"), note.Entries[0]);   // solo la prima riga dopo i trattini
    }

    [Fact]
    public void A_qr_like_image_with_a_light_corner_passes_as_it_is()
    {
        // Bordo scuro solo in alto a destra e in basso a sinistra: l'angolo in alto a sinistra è chiaro.
        var modules = new bool[21 * 21];
        modules[20] = modules[20 * 21] = true;
        var png = QrImage.Encode(modules, 21);
        Assert.NotNull(QrImage.Decode(png));
        Assert.Null(QrImage.ToModules(png));
    }
}
