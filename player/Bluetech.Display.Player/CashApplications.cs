using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Bluetech.Display.Player;

/// <summary>
/// Le applicazioni con una finestra aperta su questo PC, per scegliere quella della cassa nella configurazione, e la
/// finestra in primo piano di un'applicazione scelta, per ridarle la finestra attiva.
/// </summary>
static class CashApplications
{
    public sealed class Item
    {
        public Item(string processName, string title)
        {
            ProcessName = processName;
            Title = title;
        }

        /// <summary>Il nome del processo senza ".exe" (Process.ProcessName): quello che si salva nella configurazione.</summary>
        public string ProcessName { get; }
        public string Title { get; }

        public override string ToString() => string.IsNullOrEmpty(Title) ? ProcessName : $"{ProcessName} — {Title}";
    }

    /// <summary>Un elemento per applicazione: le finestre principali visibili, con un titolo, dalla più in alto.</summary>
    public static List<Item> Open()
    {
        var own = Process.GetCurrentProcess().Id;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Item>();
        foreach (var window in TopWindows())
        {
            var title = Title(window);
            if (title.Length == 0)
                continue;
            var name = ProcessName(window, out var pid);
            if (name == null || pid == own || !seen.Add(name))
                continue;
            result.Add(new Item(name, title));
        }
        return result.OrderBy(i => i.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// La finestra dell'applicazione indicata più in alto tra quelle visibili e non ridotte a icona, o IntPtr.Zero. Nell'
    /// ordine di Windows una finestra di dialogo aperta (il pagamento, per esempio) sta sopra la finestra principale: è
    /// quella che deve riavere la tastiera.
    /// </summary>
    public static IntPtr TopWindowOf(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return IntPtr.Zero;
        foreach (var window in TopWindows())
        {
            if (NativeMethods.IsIconic(window))
                continue;
            if (string.Equals(ProcessName(window, out _), processName, StringComparison.OrdinalIgnoreCase))
                return window;
        }
        return IntPtr.Zero;
    }

    /// <summary>Il nome del processo della finestra, senza ".exe", o null se non si sa (processo già chiuso, protetto).</summary>
    public static string ProcessName(IntPtr window, out int processId)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        processId = (int)pid;
        if (pid == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Le finestre di primo livello visibili, dall'alto in basso, senza le finestre strumento.</summary>
    static List<IntPtr> TopWindows()
    {
        var windows = new List<IntPtr>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window)
                && (NativeMethods.GetWindowLong(window, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) == 0)
                windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    static string Title(IntPtr window)
    {
        var length = NativeMethods.GetWindowTextLength(window);
        if (length <= 0)
            return "";
        var text = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(window, text, text.Capacity);
        return text.ToString().Trim();
    }
}
