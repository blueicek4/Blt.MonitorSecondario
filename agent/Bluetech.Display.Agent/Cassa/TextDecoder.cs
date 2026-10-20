using System.Text;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>
/// Testo del file con la codifica giusta: UTF-8 (con o senza BOM) se valido, altrimenti Windows-1252, l'ANSI italiano.
/// Con le lettere accentate ammesse nel modello XML (CaratteriAmmessi) la cassa può scrivere l'una o l'altra.
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
