using System.Text;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>
/// Testo del file con la codifica giusta: UTF-8 (con o senza BOM) se valido, altrimenti Windows-1252, l'ANSI italiano.
/// Con le lettere accentate ammesse nel modello XML (CaratteriAmmessi) la cassa può scrivere l'una o l'altra. UTF-16
/// con BOM non è mai stato visto, ma lo leggevano il vecchio monitor e il registratore: si legge anche qui.
/// </summary>
public static class TextDecoder
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    static readonly Lazy<Encoding> Windows1252 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    });

    public static string Decode(byte[] bytes, out string encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = "UTF-8 con BOM";
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = "UTF-16 LE con BOM";
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = "UTF-16 BE con BOM";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.All(b => b < 0x80))
        {
            encoding = "ASCII";
            return Encoding.ASCII.GetString(bytes);
        }
        try
        {
            var text = StrictUtf8.GetString(bytes);
            encoding = "UTF-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = "Windows-1252";
            return Windows1252.Value.GetString(bytes);
        }
    }
}
