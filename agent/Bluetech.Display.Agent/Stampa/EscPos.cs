using System.Globalization;
using System.Text;

namespace Bluetech.Display.Agent.Stampa;

/// <summary>
/// La copia nei comandi ESC/POS delle stampanti comande termiche (le Epson TM sono il riferimento): testo con
/// allineamento, grassetto e doppia misura, il QR come immagine raster (GS v 0, così non serve leggerne il contenuto e
/// esce uguale su ogni modello), avanzamento e taglio.
/// </summary>
public static class EscPos
{
    const byte Esc = 0x1B;
    const byte Gs = 0x1D;

    public static byte[] Encode(IReadOnlyList<CopyBlock> blocks, PaperCopyOptions options)
    {
        var (table, encoding, plain) = CharacterTable(options.TabellaCaratteri);
        var dots = options.LarghezzaMm <= 58 ? 384 : 576;
        var output = new MemoryStream();
        output.Write([Esc, (byte)'@']);                     // inizializza
        if (table >= 0)
            output.Write([Esc, (byte)'t', (byte)table]);    // tabella caratteri
        foreach (var block in blocks)
        {
            switch (block)
            {
                case CopyText text:
                    output.Write([Esc, (byte)'a', Align(text.Align)]);
                    output.Write([Esc, (byte)'E', text.Bold ? (byte)1 : (byte)0]);
                    output.Write([Gs, (byte)'!', text.Large ? (byte)0x11 : (byte)0x00]);
                    output.Write(encoding.GetBytes(plain ? Ascii(text.Text) : text.Text));
                    output.WriteByte(0x0A);
                    break;
                case CopyQr qr:
                    output.Write([Esc, (byte)'a', 1]);
                    output.Write(Raster(qr, options.QrPuntiPerModulo, dots));
                    output.WriteByte(0x0A);
                    break;
            }
        }
        output.Write([Esc, (byte)'a', 0, Esc, (byte)'E', 0, Gs, (byte)'!', 0]);
        output.Write([Esc, (byte)'d', 4]);                  // avanza 4 righe, oltre la taglierina
        if (options.Taglio)
            output.Write([Gs, (byte)'V', 1]);               // taglio parziale
        return output.ToArray();
    }

    static byte Align(CopyAlign align) => align switch
    {
        CopyAlign.Center => 1,
        CopyAlign.Right => 2,
        _ => 0,
    };

    /// <summary>GS v 0: il QR con la quiet zone di 4 moduli, ingrandito di un multiplo intero.</summary>
    static byte[] Raster(CopyQr qr, int dotsPerModule, int maxDots)
    {
        int scale, quiet;
        if (qr.Modules)
        {
            scale = Math.Clamp(dotsPerModule, 1, 10);
            while (scale > 1 && (qr.Width + 8) * scale > maxDots)
                scale--;
            quiet = 4 * scale;
        }
        else
        {
            scale = 1;   // immagine non ridotta: ogni elemento è già un pixel
            quiet = 16;
        }
        var width = qr.Width * scale + 2 * quiet;
        var height = qr.Height * scale + 2 * quiet;
        var bytesPerRow = (width + 7) / 8;
        var data = new byte[8 + bytesPerRow * height];
        data[0] = Gs;
        data[1] = (byte)'v';
        data[2] = (byte)'0';
        data[3] = 0;
        data[4] = (byte)(bytesPerRow & 0xFF);
        data[5] = (byte)(bytesPerRow >> 8);
        data[6] = (byte)(height & 0xFF);
        data[7] = (byte)(height >> 8);
        for (var y = 0; y < height; y++)
        {
            var my = (y - quiet) / scale;
            if (y < quiet || my >= qr.Height)
                continue;
            for (var x = quiet; x < quiet + qr.Width * scale; x++)
            {
                if (qr.Dark[my * qr.Width + (x - quiet) / scale])
                    data[8 + y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return data;
    }

    static (int Table, Encoding Encoding, bool Plain) CharacterTable(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return name.Trim().ToLowerInvariant() switch
        {
            "858" => (19, Encoding.GetEncoding(858), false),
            "ascii" => (-1, Encoding.ASCII, true),
            _ => (16, Encoding.GetEncoding(1252), false),
        };
    }

    /// <summary>Senza tabella caratteri: le vocali accentate come le scrive la cassa ("Maesta'"), l'euro come EUR.</summary>
    static string Ascii(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                result.Append('\'');
                continue;
            }
            result.Append(c switch
            {
                '€' => "EUR",
                < (char)0x80 => c.ToString(),
                _ => "?",
            });
        }
        return result.ToString();
    }
}
