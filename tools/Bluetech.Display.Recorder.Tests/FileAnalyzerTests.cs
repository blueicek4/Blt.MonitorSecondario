using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

public class FileAnalyzerTests
{
    // 1×1 pixel PNG
    const string TinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    static byte[] Sample => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "scontrino-bar-9-righe.txt"));

    [Fact]
    public void Real_receipt_from_Passepartout_is_parsed_like_the_old_monitor()
    {
        var a = FileAnalyzer.Analyze(Sample);

        Assert.Equal(FileKind.Scontrino, a.Kind);
        Assert.Equal("ASCII", a.Encoding);
        Assert.Equal("CRLF", a.LineEndings);
        Assert.Equal(16, a.Lines.Length);                 // come File.ReadAllLines: l'a capo finale non conta
        Assert.Equal(9, a.ReceiptLines.Count);
        Assert.Equal(" SPREMUTA ARANCIA         4,00", a.ReceiptLines[0]);
        Assert.Equal(" CAPPUCCINO               1,80", a.ReceiptLines[8]);
        Assert.Equal("19,60", a.Subtotal);
        Assert.Equal("19,60", a.Total);
    }

    [Fact]
    public void Restaurant_receipt_keeps_mixed_case_and_descriptions_cut_at_30_columns()
    {
        var a = FileAnalyzer.Analyze(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "scontrino-ristorante-3-righe.txt")));
        Assert.Equal(FileKind.Scontrino, a.Kind);
        Assert.Equal(new[] { " Tagliata Al Rosmarino   15,50", " Hamburger Vecchia Malg  16,50", " Gelato alla crema con    3,50" }, a.ReceiptLines);
        Assert.All(a.ReceiptLines, l => Assert.Equal(30, l.Length));
        Assert.Equal("35,50", a.Total);
    }

    static byte[] Recorded(string prefix) =>
        File.ReadAllBytes(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples", "registrazioni", "2026-10-20_002138"), prefix + "_*.txt").Single());

    [Fact]
    public void Receipt_at_48_columns_with_header_quantity_and_payment()
    {
        var a = FileAnalyzer.Analyze(Recorded("0009"));
        Assert.Equal(FileKind.Scontrino, a.Kind);
        Assert.Equal("ASCII", a.Encoding);
        Assert.Equal("87,00", a.Total);
        Assert.Equal("87,00", a.Subtotal);
        Assert.Contains("                  Da inserire", a.ReceiptLines);   // intestazione centrata (spazi finali tolti)
        Assert.Contains("   2 X 3,00 Euro", a.ReceiptLines);
        Assert.Contains(" Cestino di tigelle 4 pezzi                 6,00", a.ReceiptLines);
        Assert.Contains("PAGATO CONTANTI:   87,00", a.ReceiptLines);
        Assert.All(a.ReceiptLines.Where(l => l.Contains(",00") && !l.StartsWith("PAGATO") && !l.StartsWith("Resto") && !l.StartsWith("  ")), l => Assert.Equal(48, l.Length));
    }

    [Fact]
    public void Paid_bill_with_three_digit_amounts_from_the_ab_test()
    {
        var file = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples", "registrazioni", "2026-10-20_064854"), "0028_*.txt").Single();
        var a = FileAnalyzer.Analyze(File.ReadAllBytes(file));
        Assert.Equal("339,50", a.Total);
        Assert.Equal("339,50", a.Subtotal);
        Assert.Contains("PAGATO CONTANTI:  450,00", a.ReceiptLines);
        Assert.Contains("Resto:            110,50", a.ReceiptLines);
        Assert.Contains(" Caesar salad con salsa Vecchia Malga      22,00", a.ReceiptLines);
    }

    static byte[] Recorded(string session, string prefix) =>
        File.ReadAllBytes(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples", "registrazioni", session), prefix + "_*.txt").Single());

    [Fact]
    public void Satispay_qr_as_written_by_the_till()
    {
        // Geometria del QR vero (sostituito nel campione): PNG 197x197, Base64 su una riga, una riga di spazi dopo.
        var a = FileAnalyzer.Analyze(Recorded("2026-10-20_075632", "0008"));
        Assert.Equal(FileKind.QrCode, a.Kind);
        Assert.True(a.Qr.IsBase64);
        Assert.Equal("png", a.Qr.Format);
        Assert.Equal(197, a.Qr.Width);
        Assert.Equal(197, a.Qr.Height);
        Assert.Equal(2, a.Qr.LinesAfter);
    }

    [Fact]
    public void Promotion_appears_only_on_the_final_receipt_spread_under_the_lines()
    {
        var a = FileAnalyzer.Analyze(Recorded("2026-10-20_075632", "0019"));
        Assert.Equal("5,20", a.Total);
        Assert.Equal("5,20", a.Subtotal);
        Assert.Contains("                SCONTO   -0,41", a.ReceiptLines);
        Assert.Contains("PAGATO CONTANTI:    5,20", a.ReceiptLines);
        // Righe con un importo (le altre, centrate, perdono gli spazi finali), tranne pagamento e resto.
        var amounts = a.ReceiptLines.Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @",\d\d$") && !l.StartsWith("PAGATO") && !l.StartsWith("Resto")).ToList();
        Assert.Equal(4, amounts.Count);   // due articoli e i due sconti; subtotale e totale sono a parte
        Assert.All(amounts, l => Assert.Equal(30, l.Length));
    }

    [Fact]
    public void Discount_on_the_subtotal_lowers_the_total()
    {
        var a = FileAnalyzer.Analyze(Recorded("0031"));
        Assert.Equal("43,00", a.Subtotal);
        Assert.Equal("38,00", a.Total);
        Assert.Contains("SCONTO                                     -5,00", a.ReceiptLines);
    }

    [Fact]
    public void Utf8_with_bom_is_detected()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("SNF\r\n TIRAMISÙ   5,00 €\r\n")).ToArray();
        var a = FileAnalyzer.Analyze(bytes);
        Assert.Equal("UTF-8 con BOM", a.Encoding);
        Assert.Equal(" TIRAMISÙ   5,00 €", a.ReceiptLines.Single());
    }

    [Fact]
    public void Utf8_without_bom_is_detected()
    {
        var a = FileAnalyzer.Analyze(Encoding.UTF8.GetBytes("SNF\n CAFFÈ 1,20\n"));
        Assert.Equal("UTF-8 senza BOM", a.Encoding);
        Assert.Equal("LF", a.LineEndings);
        Assert.Equal(" CAFFÈ 1,20", a.ReceiptLines.Single());
    }

    [Fact]
    public void Ansi_windows_1252_is_detected_and_decoded()
    {
        // € = 0x80, È = 0xC8, ù = 0xF9 in Windows-1252: non sono UTF-8 valido
        var bytes = Encoding.ASCII.GetBytes("SNF\r\n ").Concat(new byte[] { 0x80, 0x20, 0xC8, 0x20, 0xF9 }).Concat(Encoding.ASCII.GetBytes(" 1,00\r\n")).ToArray();
        var a = FileAnalyzer.Analyze(bytes);
        Assert.Equal("ANSI Windows-1252", a.Encoding);
        Assert.Equal(" € È ù 1,00", a.ReceiptLines.Single());
    }

    [Fact]
    public void Display_message_lines_are_joined_like_the_old_monitor()
    {
        var a = FileAnalyzer.Analyze(Encoding.ASCII.GetBytes("TOTALE\r\n   5,80  \r\n"));
        Assert.Equal(FileKind.Messaggio, a.Kind);
        Assert.Equal("TOTALE 5,80", a.DisplayText);
        Assert.False(a.HeaderNotExact);
    }

    [Fact]
    public void Header_with_spaces_is_not_a_receipt_for_the_old_monitor()
    {
        var a = FileAnalyzer.Analyze(Encoding.ASCII.GetBytes("SNF \r\n CAFFE 1,00\r\n"));
        Assert.Equal(FileKind.Messaggio, a.Kind);
        Assert.True(a.HeaderNotExact);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\nSNF\r\n")]
    public void Empty_or_blank_first_line_is_ignored(string text)
    {
        Assert.Equal(FileKind.Vuoto, FileAnalyzer.Analyze(Encoding.ASCII.GetBytes(text)).Kind);
    }

    [Fact]
    public void Qr_code_in_base64_is_decoded_as_image()
    {
        var a = FileAnalyzer.Analyze(Encoding.ASCII.GetBytes("SNF\r\nQRCODE\r\n" + TinyPng + "\r\n"));
        Assert.Equal(FileKind.QrCode, a.Kind);
        Assert.True(a.Qr.IsBase64);
        Assert.Equal("png", a.Qr.Format);
        Assert.Equal(1, a.Qr.Width);
        Assert.Equal(1, a.Qr.Height);
        Assert.Equal(1, a.Qr.LinesAfter);
    }

    [Fact]
    public void Qr_code_with_plain_text_is_kept_as_text()
    {
        var a = FileAnalyzer.Analyze(Encoding.ASCII.GetBytes("SNF\r\nQRCODE\r\nhttps://example.com/pay?id=1\r\n"));
        Assert.Equal(FileKind.QrCode, a.Kind);
        Assert.False(a.Qr.IsBase64);
        Assert.Equal("https://example.com/pay?id=1", a.Qr.TextPreview);
    }

    [Fact]
    public void Line_key_ignores_padding()
    {
        Assert.Equal(FileAnalyzer.LineKey(" CAPPUCCINO               1,80"), FileAnalyzer.LineKey("CAPPUCCINO 1,80 "));
    }
}
