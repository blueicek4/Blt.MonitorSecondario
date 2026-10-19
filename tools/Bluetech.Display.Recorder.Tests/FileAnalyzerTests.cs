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
