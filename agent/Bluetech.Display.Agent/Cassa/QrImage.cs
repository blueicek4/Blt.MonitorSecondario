using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Bluetech.Display.Agent.Cassa;

/// <summary>
/// Il QR di Satispay arriva come PNG 197×197: 49 moduli da 4 pixel, un pixel bianco in più a destra e in basso, nessuna
/// quiet zone. Il display (<c>QrStage</c>) lo vuole a un pixel per modulo, per ingrandirlo di un multiplo intero alla
/// misura che serve e aggiungere lui la quiet zone. Qui si legge il PNG, si misura il modulo sul finder pattern in alto a
/// sinistra e si riscrive N×N in scala di grigi. Un'immagine che non è un QR riconoscibile dà null: passa l'originale.
/// </summary>
static class QrImage
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[]? ToModules(byte[] png)
    {
        try
        {
            var image = Decode(png);
            return image == null ? null : Reduce(image);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    internal sealed record Bitmap(int Width, int Height, bool[] Dark);

    /// <summary>PNG non interlacciato, 8 bit per canale, oppure 1/2/4 bit in scala di grigi o a palette.</summary>
    internal static Bitmap? Decode(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            return null;

        int width = 0, height = 0, depth = 0, color = -1, interlace = 0;
        byte[]? palette = null, transparency = null;
        using var idat = new MemoryStream();
        var pos = Signature.Length;
        while (pos + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            if (length < 0 || pos + 12L + length > png.Length)
                return null;
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, length);
            if (type == "IHDR" && length >= 13)
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                depth = data[8];
                color = data[9];
                interlace = data[12];
            }
            else if (type == "PLTE")
                palette = data.ToArray();
            else if (type == "tRNS")
                transparency = data.ToArray();
            else if (type == "IDAT")
                idat.Write(data);
            else if (type == "IEND")
                break;
            pos += 12 + length;
        }

        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var depthOk = depth == 8 || (depth is 1 or 2 or 4 && color is 0 or 3);
        if (width is < 21 or > 2048 || height is < 21 or > 2048 || interlace != 0 || channels == 0 || !depthOk)
            return null;
        if (color == 3 && palette == null)
            return null;

        var bitsPerPixel = channels * depth;
        var stride = (width * bitsPerPixel + 7) / 8;
        var step = Math.Max(1, bitsPerPixel / 8);
        var raw = new byte[height * (stride + 1)];
        idat.Position = 0;
        using (var inflate = new ZLibStream(idat, CompressionMode.Decompress))
            inflate.ReadExactly(raw);

        var dark = new bool[width * height];
        var previous = new byte[stride];
        var current = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var offset = y * (stride + 1);
            Array.Copy(raw, offset + 1, current, 0, stride);
            if (!Unfilter(raw[offset], current, previous, step))
                return null;
            for (var x = 0; x < width; x++)
                dark[y * width + x] = Luma(current, x, color, depth, channels, palette, transparency) < 128;
            (previous, current) = (current, previous);
        }
        return new Bitmap(width, height, dark);
    }

    static bool Unfilter(byte filter, byte[] row, byte[] above, int step)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= step ? row[i - step] : 0, up = above[i], upLeft = i >= step ? above[i - step] : 0;
            int predictor;
            switch (filter)
            {
                case 0:
                    return true;
                case 1:
                    predictor = left;
                    break;
                case 2:
                    predictor = up;
                    break;
                case 3:
                    predictor = (left + up) >> 1;
                    break;
                case 4:
                    var p = left + up - upLeft;
                    int pa = Math.Abs(p - left), pb = Math.Abs(p - up), pc = Math.Abs(p - upLeft);
                    predictor = pa <= pb && pa <= pc ? left : pb <= pc ? up : upLeft;
                    break;
                default:
                    return false;
            }
            row[i] = (byte)(row[i] + predictor);
        }
        return true;
    }

    /// <summary>Luminosità del pixel composto su bianco (la trasparenza è carta bianca).</summary>
    static int Luma(byte[] row, int x, int color, int depth, int channels, byte[]? palette, byte[]? transparency)
    {
        int r, g, b, a = 255;
        if (depth < 8)
        {
            var bit = x * depth;
            var value = (row[bit >> 3] >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
            if (color == 3)
            {
                if (value * 3 + 2 >= palette!.Length)
                    return 255;
                (r, g, b) = (palette[value * 3], palette[value * 3 + 1], palette[value * 3 + 2]);
                a = transparency != null && value < transparency.Length ? transparency[value] : 255;
            }
            else
                r = g = b = value * 255 / ((1 << depth) - 1);
        }
        else
        {
            var i = x * channels;
            switch (color)
            {
                case 0:
                    r = g = b = row[i];
                    break;
                case 4:
                    r = g = b = row[i];
                    a = row[i + 1];
                    break;
                case 3:
                    var index = row[i];
                    if (index * 3 + 2 >= palette!.Length)
                        return 255;
                    (r, g, b) = (palette[index * 3], palette[index * 3 + 1], palette[index * 3 + 2]);
                    a = transparency != null && index < transparency.Length ? transparency[index] : 255;
                    break;
                default:
                    (r, g, b) = (row[i], row[i + 1], row[i + 2]);
                    if (color == 6)
                        a = row[i + 3];
                    break;
            }
        }
        var luma = (299 * r + 587 * g + 114 * b) / 1000;
        return (luma * a + 255 * (255 - a)) / 255;
    }

    static byte[]? Reduce(Bitmap image)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            if (!image.Dark[y * image.Width + x])
                continue;
            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }
        var size = right - left + 1;
        if (right < 0 || bottom - top + 1 != size)
            return null;

        // Prima riga del finder pattern in alto a sinistra: 7 moduli scuri, poi il separatore chiaro.
        var run = 0;
        while (left + run <= right && image.Dark[top * image.Width + left + run])
            run++;
        if (run % 7 != 0 || size % (run / 7) != 0)
            return null;
        var module = run / 7;
        var n = size / module;
        if (n is < 21 or > 177 || (n - 17) % 4 != 0)
            return null;

        var modules = new bool[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
            modules[y * n + x] = image.Dark[(top + y * module + module / 2) * image.Width + left + x * module + module / 2];

        return Finder(modules, n, 0, 0) && Finder(modules, n, n - 7, 0) && Finder(modules, n, 0, n - 7) ? Encode(modules, n) : null;
    }

    /// <summary>Il finder pattern 7×7: bordo scuro, anello chiaro, centro 3×3 scuro.</summary>
    static bool Finder(bool[] modules, int n, int left, int top)
    {
        for (var dy = 0; dy < 7; dy++)
        for (var dx = 0; dx < 7; dx++)
        {
            var ring = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
            if (modules[(top + dy) * n + left + dx] != (ring != 2))
                return false;
        }
        return true;
    }

    /// <summary>PNG in scala di grigi a 8 bit, un pixel per modulo: nero 0, bianco 255.</summary>
    static byte[] Encode(bool[] modules, int n)
    {
        var raw = new byte[n * (n + 1)];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
            raw[y * (n + 1) + 1 + x] = modules[y * n + x] ? (byte)0 : (byte)255;

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(raw);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, n);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), n);
        header[8] = 8;   // bit per canale
        header[9] = 0;   // scala di grigi

        using var png = new MemoryStream();
        png.Write(Signature);
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        output.Write(number);
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        var c = (uint)i;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
