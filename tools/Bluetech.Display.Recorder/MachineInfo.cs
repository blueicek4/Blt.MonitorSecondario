using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Bluetech.Display.Recorder;

/// <summary>Dati del PC cassa utili al progetto: Windows, schermi, WebView2. Nessun dato personale.</summary>
static class MachineInfo
{
    public static string Describe()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine("PC");
            sb.AppendLine("  Sistema: " + WindowsVersion());
            sb.AppendLine($"  64 bit: {(Environment.Is64BitOperatingSystem ? "sì" : "no")} · processori logici: {Environment.ProcessorCount}");
            sb.AppendLine("  WebView2 Runtime: " + (WebView2Version() ?? "non trovato"));
#if NETFRAMEWORK
            sb.AppendLine("Schermi");
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                sb.AppendLine($"  {i + 1}. {s.DeviceName} {s.Bounds.Width}×{s.Bounds.Height} in ({s.Bounds.X},{s.Bounds.Y}){(s.Primary ? " — principale" : "")}");
            }
#endif
        }
        catch (Exception ex)
        {
            sb.AppendLine("  (informazioni incomplete: " + ex.Message + ")");
        }
        return sb.ToString();
    }

    static string WindowsVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSDescription;

        var product = Registry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
        var display = Registry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion")
                      ?? Registry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");
        var build = Registry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
        // Windows 11 si presenta ancora come "Windows 10" in ProductName: lo distingue il numero di build.
        if (product != null && int.TryParse(build, out var number) && number >= 22000)
            product = product.Replace("Windows 10", "Windows 11");
        return $"{product ?? Environment.OSVersion.ToString()} {display} (build {build})";
    }

    static string WebView2Version()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;
        const string client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var key in new[] { @"SOFTWARE\WOW6432Node\" + client, @"SOFTWARE\" + client })
        {
            var version = Registry(key, "pv") ?? Registry(key, "pv", currentUser: true);
            if (!string.IsNullOrEmpty(version) && version != "0.0.0.0")
                return version;
        }
        return null;
    }

    static string Registry(string path, string name, bool currentUser = false)
    {
#if NETFRAMEWORK
        try
        {
            var root = currentUser ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name)?.ToString();
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }
}

/// <summary>Accorgimenti per la console di Windows: DPI reale degli schermi e chiusura con la X.</summary>
static class WindowsConsole
{
    delegate bool ConsoleCtrlHandler(int ctrlType);

    const int CtrlCloseEvent = 2;
    const int CtrlLogoffEvent = 5;
    const int CtrlShutdownEvent = 6;

    static ConsoleCtrlHandler _handler;   // tenuto vivo per il garbage collector

    [DllImport("kernel32.dll")]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler handler, bool add);

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    /// <summary>Senza DPI awareness Windows riporterebbe risoluzioni "scalate" invece di quelle reali.</summary>
    public static void Prepare()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        try
        {
            if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))   // per-monitor v2
                SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            try { SetProcessDPIAware(); } catch { }
        }
        catch { }
    }

    /// <summary>Chiusura della finestra, disconnessione o spegnimento: Windows concede qualche secondo per salvare.</summary>
    public static void OnClose(Action action)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        _handler = type =>
        {
            if (type is CtrlCloseEvent or CtrlLogoffEvent or CtrlShutdownEvent)
            {
                action();
                return true;
            }
            return false;   // Ctrl+C e Ctrl+Interr li gestisce Console.CancelKeyPress
        };
        try { SetConsoleCtrlHandler(_handler, true); } catch { }
    }
}
