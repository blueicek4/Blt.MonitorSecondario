#if NETFRAMEWORK
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#endif

namespace Bluetech.Display.Recorder;

public static class ScreenCamera
{
    /// <summary>La fotocamera dello schermo di Windows, o null altrove (i test girano anche su Linux).</summary>
    public static IScreenCamera Create()
    {
#if NETFRAMEWORK
        return WindowsConsole.IsWindows ? new WindowsScreenCamera() : null;
#else
        return null;
#endif
    }
}

#if NETFRAMEWORK
/// <summary>
/// Tutti gli schermi in un JPEG (la DPI awareness del processo, impostata all'avvio, dà i pixel veri). Oltre i 2560 px
/// di larghezza si riduce: con due schermi affiancati il file resta sui 300-400 KB e i testi si leggono ancora.
/// </summary>
sealed class WindowsScreenCamera : IScreenCamera
{
    const int MaxWidth = 2560;
    const long JpegQuality = 70;
    const int GridWidth = 96;
    const int GridHeight = 54;

    static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public ScreenShot Take()
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;
        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try
        {
            using var g = Graphics.FromImage(full);
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;   // desktop bloccato o non disponibile
        }

        var scale = Math.Min(1.0, (double)MaxWidth / bounds.Width);
        var width = (int)Math.Round(bounds.Width * scale);
        var height = (int)Math.Round(bounds.Height * scale);
        byte[] jpeg;
        using (var output = new MemoryStream())
        {
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            if (scale < 1.0)
            {
                using var small = Resize(full, width, height);
                small.Save(output, Jpeg, parameters);
            }
            else
            {
                full.Save(output, Jpeg, parameters);
            }
            jpeg = output.ToArray();
        }
        return new ScreenShot { Jpeg = jpeg, Fingerprint = Fingerprint(full, bounds), Width = width, Height = height };
    }

    static Bitmap Resize(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return result;
    }

    /// <summary>
    /// Luminosità media di una griglia 96×54. Le celle sotto la finestra del registratore valgono 0: il cursore che
    /// lampeggia e le righe nuove a video non devono far scattare una foto al secondo.
    /// </summary>
    static byte[] Fingerprint(Bitmap full, Rectangle bounds)
    {
        using var grid = new Bitmap(GridWidth, GridHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(grid))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(full, 0, 0, GridWidth, GridHeight);
        }
        var result = new byte[GridWidth * GridHeight];
        var data = grid.LockBits(new Rectangle(0, 0, GridWidth, GridHeight), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < GridHeight; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (var x = 0; x < GridWidth; x++)
                {
                    var b = row[x * 3];
                    var gr = row[x * 3 + 1];
                    var r = row[x * 3 + 2];
                    result[y * GridWidth + x] = (byte)((r * 299 + gr * 587 + b * 114) / 1000);
                }
            }
        }
        finally
        {
            grid.UnlockBits(data);
        }

        if (ConsoleRectangle() is { } console)
        {
            // Dalla finestra (coordinate dello schermo virtuale) alle celle della griglia, arrotondando verso l'esterno.
            var left = Math.Max(0, (int)Math.Floor((console.Left - bounds.Left) * (double)GridWidth / bounds.Width));
            var right = Math.Min(GridWidth, (int)Math.Ceiling((console.Right - bounds.Left) * (double)GridWidth / bounds.Width));
            var top = Math.Max(0, (int)Math.Floor((console.Top - bounds.Top) * (double)GridHeight / bounds.Height));
            var bottom = Math.Min(GridHeight, (int)Math.Ceiling((console.Bottom - bounds.Top) * (double)GridHeight / bounds.Height));
            for (var y = top; y < bottom; y++)
                for (var x = left; x < right; x++)
                    result[y * GridWidth + x] = 0;
        }
        return result;
    }

    static Rectangle? ConsoleRectangle()
    {
        try
        {
            var window = GetConsoleWindow();
            if (window == IntPtr.Zero || IsIconic(window) || !GetWindowRect(window, out var r))
                return null;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);
}
#endif
