using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bluetech.Display.Recorder;

public enum FileKind
{
    /// <summary>File vuoto o con la prima riga vuota: il vecchio monitor lo ignora.</summary>
    Vuoto,
    /// <summary>Prima riga "SNF": scontrino.</summary>
    Scontrino,
    /// <summary>Prima riga "SNF" e una riga "QRCODE": pagamento con QR (Satispay).</summary>
    QrCode,
    /// <summary>Qualunque altro contenuto: messaggio del visore cliente.</summary>
    Messaggio,
}

public sealed class QrInfo
{
    public int LinesAfter { get; set; }
    public int PayloadLength { get; set; }
    public bool IsBase64 { get; set; }
    public byte[] Data { get; set; }
    /// <summary>png, jpg, gif, bmp oppure bin se i byte non sono un'immagine nota.</summary>
    public string Format { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Inizio del contenuto quando non è Base64 (potrebbe essere un link o un codice Satispay).</summary>
    public string TextPreview { get; set; }
}

public sealed class FileAnalysis
{
    public string Encoding { get; set; }
    public string LineEndings { get; set; }
    public string[] Lines { get; set; }
    public FileKind Kind { get; set; }
    /// <summary>La prima riga, senza spazi, è SNF ma non esattamente "SNF": il vecchio monitor non la riconoscerebbe.</summary>
    public bool HeaderNotExact { get; set; }
    /// <summary>Righe dello scontrino (articoli, sconti, note…) senza righe vuote, subtotale e totale.</summary>
    public List<string> ReceiptLines { get; } = new();
    public string Subtotal { get; set; }
    public string Total { get; set; }
    public string DisplayText { get; set; }
    public QrInfo Qr { get; set; }
}

public static class FileAnalyzer
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static FileAnalysis Analyze(byte[] bytes)
    {
        var analysis = new FileAnalysis();
        var text = Decode(bytes, out var encoding);
        analysis.Encoding = encoding;
        analysis.LineEndings = DescribeLineEndings(text);
        analysis.Lines = SplitLines(text);

        var lines = analysis.Lines;
        if (lines.Length == 0 || lines[0].Length == 0)
        {
            analysis.Kind = FileKind.Vuoto;
            return analysis;
        }

        // Stesse regole del vecchio MonitorCliente: confronto esatto su "SNF" e "QRCODE".
        if (lines[0] == "SNF")
        {
            var qrIndex = Array.IndexOf(lines, "QRCODE");
            if (qrIndex >= 0)
            {
                analysis.Kind = FileKind.QrCode;
                analysis.Qr = AnalyzeQr(lines, qrIndex);
                return analysis;
            }

            analysis.Kind = FileKind.Scontrino;
            ParseReceipt(lines, analysis);
            return analysis;
        }

        analysis.HeaderNotExact = lines[0].Trim() == "SNF";
        analysis.Kind = FileKind.Messaggio;
        analysis.DisplayText = lines.Aggregate((a, b) => a + " " + b.Trim()).Trim();
        return analysis;
    }

    /// <summary>Chiave di confronto di una riga: senza spazi ai bordi e con gli spazi interni compattati.</summary>
    public static string LineKey(string line) => Spaces.Replace(line.Trim(), " ");

    static void ParseReceipt(string[] lines, FileAnalysis analysis)
    {
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            if (trimmed.StartsWith("SUBTOT", StringComparison.Ordinal))
            {
                analysis.Subtotal = LastToken(trimmed);
                continue;
            }
            if (trimmed.StartsWith("TOT. DA PAGARE", StringComparison.Ordinal))
            {
                analysis.Total = LastToken(trimmed);
                continue;
            }
            analysis.ReceiptLines.Add(line.TrimEnd());
        }
    }

    static string LastToken(string trimmed)
    {
        var space = trimmed.LastIndexOf(' ');
        return space < 0 ? trimmed : trimmed.Substring(space + 1);
    }

    static QrInfo AnalyzeQr(string[] lines, int qrIndex)
    {
        var info = new QrInfo { LinesAfter = lines.Length - qrIndex - 1 };
        var payload = qrIndex + 1 < lines.Length ? lines[qrIndex + 1].Trim() : "";
        info.PayloadLength = payload.Length;
        if (payload.Length == 0)
            return info;

        try
        {
            info.Data = Convert.FromBase64String(payload);
            info.IsBase64 = true;
        }
        catch (FormatException)
        {
            info.TextPreview = payload.Length > 200 ? payload.Substring(0, 200) + "…" : payload;
            return info;
        }

        info.Format = DetectImage(info.Data, out var width, out var height);
        info.Width = width;
        info.Height = height;
        return info;
    }

    public static string DetectImage(byte[] data, out int width, out int height)
    {
        width = height = 0;
        if (Starts(data, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
        {
            if (data.Length >= 24)
            {
                width = BigEndian(data, 16);
                height = BigEndian(data, 20);
            }
            return "png";
        }
        if (Starts(data, 0xFF, 0xD8, 0xFF))
            return "jpg";
        if (Starts(data, 0x47, 0x49, 0x46, 0x38))
        {
            if (data.Length >= 10)
            {
                width = data[6] | data[7] << 8;
                height = data[8] | data[9] << 8;
            }
            return "gif";
        }
        if (Starts(data, 0x42, 0x4D))
        {
            if (data.Length >= 26)
            {
                width = BitConverter.ToInt32(data, 18);
                height = Math.Abs(BitConverter.ToInt32(data, 22));
            }
            return "bmp";
        }
        return "bin";
    }

    static bool Starts(byte[] data, params byte[] prefix) =>
        data.Length >= prefix.Length && !prefix.Where((b, i) => data[i] != b).Any();

    static int BigEndian(byte[] d, int i) => d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];

    /// <summary>Decodifica rilevando BOM, UTF-8 valido, altrimenti Windows-1252 (ANSI italiano).</summary>
    public static string Decode(byte[] bytes, out string encoding)
    {
        if (Starts(bytes, 0xEF, 0xBB, 0xBF))
        {
            encoding = "UTF-8 con BOM";
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (Starts(bytes, 0xFF, 0xFE))
        {
            encoding = "UTF-16 LE con BOM";
            return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (Starts(bytes, 0xFE, 0xFF))
        {
            encoding = "UTF-16 BE con BOM";
            return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.All(b => b < 0x80))
        {
            encoding = "ASCII";
            return System.Text.Encoding.ASCII.GetString(bytes);
        }
        try
        {
            var text = StrictUtf8.GetString(bytes);
            encoding = "UTF-8 senza BOM";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = "ANSI Windows-1252";
            return Windows1252.GetString(bytes);
        }
    }

    static Encoding Windows1252
    {
        get
        {
#if !NETFRAMEWORK
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
            return System.Text.Encoding.GetEncoding(1252);
        }
    }

    static string[] SplitLines(string text)
    {
        if (text.Length == 0)
            return new string[0];
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // L'a capo finale non crea una riga in più (come File.ReadAllLines del vecchio monitor).
        return text.EndsWith("\n") || text.EndsWith("\r") ? lines.Take(lines.Length - 1).ToArray() : lines;
    }

    static string DescribeLineEndings(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = new List<string>();
        if (crlf > 0) kinds.Add("CRLF");
        if (lf > 0) kinds.Add("LF");
        if (cr > 0) kinds.Add("CR");
        return kinds.Count == 0 ? "nessuno" : string.Join("+", kinds);
    }
}
