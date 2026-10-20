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
}
