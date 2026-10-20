using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Bluetech.Display.Player;

/// <summary>Log giornaliero in ProgramData\Bluetech\DisplayPlayer\logs, conservato 14 giorni.</summary>
static class Log
{
    static readonly object Sync = new();
    static string Folder => Path.Combine(PlayerSettings.Folder, "logs");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception ex = null) =>
        Write("ERROR", ex == null ? message : message + " — " + ex);

    public static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Folder))
                return;
            foreach (var file in new DirectoryInfo(Folder).GetFiles("player-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14)))
                file.Delete();
        }
        catch { }
    }

    static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                PlayerSettings.EnsureFolder();
                Directory.CreateDirectory(Folder);
                File.AppendAllText(
                    Path.Combine(Folder, "player-" + DateTime.Now.ToString("yyyyMMdd") + ".log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine);
            }
        }
        catch { }
    }
}

/// <summary>Avvio automatico all'accesso dell'utente Windows (chiave Run di HKCU).</summary>
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Bluetech Display Player";

    static string Command => "\"" + Application.ExecutablePath + "\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName);
    }
}

/// <summary>Scorciatoia globale (Ctrl+Alt+Maiusc+F12): funziona anche se il player non ha il focus.</summary>
sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int Id = 0xB17;
    bool _registered;

    public event EventHandler Pressed;

    public HotkeyWindow() => CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });

    public bool Register(uint modifiers, Keys key) =>
        _registered = NativeMethods.RegisterHotKey(Handle, Id, modifiers | NativeMethods.MOD_NOREPEAT, (uint)key);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == Id)
            Pressed?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }
}

static class ScreenLocator
{
    /// <summary>
    /// Lo schermo scelto se collegato, altrimenti il primo secondario. Il principale solo se scelto apposta: in automatico
    /// il monitor cliente non copre mai la cassa.
    /// </summary>
    public static Screen Find(PlayerSettings settings)
    {
        var screens = Screen.AllScreens;
        if (settings.Screen == PlayerSettings.PrimaryScreen)
            return Screen.PrimaryScreen;
        if (!string.IsNullOrEmpty(settings.Screen))
        {
            var chosen = screens.FirstOrDefault(s => string.Equals(s.DeviceName, settings.Screen, StringComparison.OrdinalIgnoreCase));
            if (chosen != null)
                return chosen;
        }
        return screens.Where(s => !s.Primary).OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).FirstOrDefault();
    }

    public static string Describe(Screen screen, int index) =>
        $"{index + 1}: {screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? " — principale" : "")}  ({screen.DeviceName})";
}

static class AppIcon
{
    static Icon _icon;

    /// <summary>Icona generata a runtime finché non avremo il file .ico ufficiale Bluetech.</summary>
    public static Icon Get()
    {
        if (_icon != null)
            return _icon;

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        using (var path = new GraphicsPath())
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(37, 99, 235), Color.FromArgb(14, 165, 233), 45f))
        using (var font = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            path.AddArc(0, 0, 12, 12, 180, 90);
            path.AddArc(19, 0, 12, 12, 270, 90);
            path.AddArc(19, 19, 12, 12, 0, 90);
            path.AddArc(0, 19, 12, 12, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, "B", font, new Rectangle(0, 0, 32, 32), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        _icon = Icon.FromHandle(bitmap.GetHicon());
        return _icon;
    }
}
