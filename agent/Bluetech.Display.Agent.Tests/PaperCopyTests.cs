using System.Net;
using System.Net.Sockets;
using System.Text;
using Bluetech.Display.Agent.Cassa;
using Bluetech.Display.Agent.Stampa;
using Bluetech.Display.Agent.Vendita;
using Xunit;

namespace Bluetech.Display.Agent.Tests;

/// <summary>La copia cartacea dello scontrino digitale (docs/03 §5-6): composizione, ESC/POS, stampanti.</summary>
public class PaperCopyTests
{
    static readonly DateTimeOffset PaidAt = new(2026, 10, 20, 8, 41, 0, TimeSpan.FromHours(2));

    /// <summary>Lo scontrino del bar pagato con carta e il QR della registrazione, ridotto a 49×49.</summary>
    static DigitalReceipt Receipt(bool withBill = true) => new()
    {
        Id = "R1-TEST",
        Png = ReceiptParser.Parse(Samples.Version("2026-10-20_075632", "0008")).QrDataUri!,
        Bill = withBill ? ReceiptParser.Parse(Samples.Receipt("scontrino-bar-pagato-carta.txt")) : null,
        Caption = "Scarica il tuo scontrino digitale!",
        IssuedAt = PaidAt,
        PaperCopy = true,
    };

    static readonly PaperCopyOptions Options80 = new() { Colonne = 42 };

    [Fact]
    public void The_copy_is_in_receipt_order_with_the_small_qr_at_the_end()
    {
        var lines = PaperCopy.ToText(PaperCopy.Compose(Receipt(), Options80), 42).Split('\n');
        int Index(string text) => Array.FindIndex(lines, l => l.Contains(text, StringComparison.Ordinal));

        int[] order = [Index("LOCALE 1"), Index("P.I.00000000000"), Index("Caffe'"), Index("SCONTO"), Index("TOTALE"),
            Index("Pagato C.CREDITO"), Index("Resto"), Index("20/10/2026 08:41"), Index("[QR 49x49]"), Index("Inquadra il QR")];
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);

        Assert.Equal(" Caffe'".TrimStart() + new string(' ', 42 - 6 - 4) + "1,40", lines[Index("Caffe'")]);
        Assert.Equal(42, lines[Index("Pagato C.CREDITO")].Length);
        Assert.Equal("TOTALE           1,20", lines[Index("TOTALE")]);   // doppia misura: 21 colonne
        Assert.EndsWith("-0,20", lines[Index("SCONTO")]);
    }

    [Fact]
    public void Without_the_bill_the_copy_is_the_qr_with_its_notice()
    {
        var text = PaperCopy.ToText(PaperCopy.Compose(Receipt(withBill: false), Options80), 42);
        Assert.Contains("Scarica il tuo scontrino digitale!", text);
        Assert.Contains("[QR 49x49]", text);
        Assert.DoesNotContain("TOTALE", text);
    }

    static int IndexOf(byte[] data, byte[] pattern)
    {
        for (var i = 0; i + pattern.Length <= data.Length; i++)
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return i;
        return -1;
    }

    [Fact]
    public void Esc_pos_with_character_table_raster_qr_and_cut()
    {
        var bytes = EscPos.Encode(PaperCopy.Compose(Receipt(), Options80), Options80);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x74, 16 }, bytes[..5]);   // inizializza, Windows-1252
        Assert.Equal(new byte[] { 0x1B, 0x64, 4, 0x1D, 0x56, 1 }, bytes[^6..]);   // avanza e taglia

        // QR: 49 moduli + 4 di quiet zone per lato, 4 punti per modulo = 228 punti, 29 byte per riga.
        var raster = IndexOf(bytes, [0x1D, 0x76, 0x30, 0x00, 29, 0, 228, 0]);
        Assert.True(raster > 0);
        var firstDarkRow = raster + 8 + 16 * 29;   // la prima riga del finder pattern, dopo 16 punti di quiet zone
        Assert.Equal(0x80, bytes[firstDarkRow + 2] & 0x80);   // il punto 16 è il primo del modulo in alto a sinistra
        Assert.Equal(0, bytes[raster + 8 + 15 * 29 + 2]);     // la riga sopra è quiet zone

        Assert.True(IndexOf(bytes, Encoding.ASCII.GetBytes("TOTALE")) > 0);
    }

    [Theory]
    [InlineData("1252", new byte[] { 0x43, 0x69, 0x74, 0x74, 0xE0, 0x20, 0x80 })]
    [InlineData("858", new byte[] { 0x43, 0x69, 0x74, 0x74, 0x85, 0x20, 0xD5 })]
    [InlineData("ascii", new byte[] { 0x43, 0x69, 0x74, 0x74, 0x61, 0x27, 0x20, 0x45, 0x55, 0x52 })]
    public void Accents_and_euro_in_each_character_table(string table, byte[] expected)
    {
        var bytes = EscPos.Encode([new CopyText("Città €")], new PaperCopyOptions { TabellaCaratteri = table });
        Assert.True(IndexOf(bytes, expected) > 0, BitConverter.ToString(bytes));
    }

    [Fact]
    public void The_qr_shrinks_to_fit_a_58_mm_roll()
    {
        var options = new PaperCopyOptions { LarghezzaMm = 58, Colonne = 32, QrPuntiPerModulo = 8 };
        var bytes = EscPos.Encode(PaperCopy.Compose(Receipt(), options), options);
        // 57 moduli × 8 = 456 punti non entrano in 384: si scende a 6 punti per modulo, 342 punti, 43 byte per riga.
        Assert.True(IndexOf(bytes, [0x1D, 0x76, 0x30, 0x00, 43, 0, 0x56, 1]) > 0);
    }

    [Fact]
    public async Task The_network_printer_receives_the_copy()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Receive(listener);
        var data = Encoding.ASCII.GetBytes("ESC/POS di prova");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await new NetworkPrinter("127.0.0.1", port).PrintAsync(data, "", "R1", timeout.Token);
        Assert.Equal(data, await received);
        listener.Stop();
    }

    [Fact]
    public async Task A_busy_printer_is_retried_until_it_answers()
    {
        // La porta è libera ma ancora chiusa: la stampante "arriva" dopo mezzo secondo.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var data = Encoding.ASCII.GetBytes("copia");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var printing = new NetworkPrinter("127.0.0.1", port).PrintAsync(data, "", "R1", timeout.Token);
        await Task.Delay(500);
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var received = Receive(listener);
        await printing;
        Assert.Equal(data, await received);
        listener.Stop();
    }

    [Fact]
    public async Task A_printer_that_never_answers_fails_at_the_timeout()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NetworkPrinter("127.0.0.1", port).PrintAsync([1, 2, 3], "", "R1", timeout.Token));
    }

    [Fact]
    public async Task The_trial_printer_writes_text_and_esc_pos_next_to_the_log()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bt-copie-" + Guid.NewGuid().ToString("N"));
        try
        {
            var printer = CopyPrinters.Create(new PaperCopyOptions { Collegamento = "prova" }, folder);
            Assert.IsType<TrialPrinter>(printer);
            await printer!.PrintAsync([0x1B, 0x40], "LOCALE 1\n", "R1", CancellationToken.None);
            var files = Directory.GetFiles(Path.Combine(folder, "copie")).Select(f => Path.GetExtension(f)!).Order().ToArray();
            Assert.Equal([".bin", ".txt"], files);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("rete", "", null)]
    [InlineData("rete", "192.168.1.50", "192.168.1.50:9100")]
    [InlineData("rete", "192.168.1.50:9101", "192.168.1.50:9101")]
    [InlineData("windows", "EPSON TM-T20II", "Windows: EPSON TM-T20II")]
    public void The_printer_comes_from_the_configuration(string connection, string printer, string? description)
    {
        var created = CopyPrinters.Create(new PaperCopyOptions { Collegamento = connection, Stampante = printer }, Path.GetTempPath());
        Assert.Equal(description, created?.Description);
    }

    static async Task<byte[]> Receive(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var memory = new MemoryStream();
        await client.GetStream().CopyToAsync(memory);
        return memory.ToArray();
    }
}
