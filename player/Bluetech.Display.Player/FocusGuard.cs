using System;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

/// <summary>
/// La rete di sicurezza del monitor touch (docs/03 §7.2): a schermo intero la finestra ha WS_EX_NOACTIVATE e lo schermo
/// di vetro tiene i tocchi lontani dal browser, ma se il Player diventa comunque la finestra attiva la battuta successiva
/// e il lettore di codici a barre finirebbero sul monitor del cliente invece che in Passepartout. Qui si guarda ogni
/// cambio di finestra in primo piano (evento di sistema, senza entrare negli altri processi): se diventa il Player, la
/// finestra attiva torna subito all'applicazione della cassa indicata nella configurazione (la sua finestra più in
/// alto, anche una finestra di dialogo), oppure, senza applicazione indicata o se non è aperta, alla finestra di prima.
/// Le finestre di configurazione del Player sono altre finestre: non si toccano.
/// </summary>
sealed class FocusGuard : IDisposable
{
    readonly Form _form;
    // Tenuto in un campo: se il garbage collector lo raccogliesse, Windows chiamerebbe un puntatore non più valido.
    readonly NativeMethods.WinEventProc _callback;
    IntPtr _hook;
    // Le ultime due finestre non del Player in primo piano: se la più recente non c'è più (la configurazione del Player,
    // appena chiusa), si torna a quella prima, di solito la cassa.
    IntPtr _previous;
    IntPtr _older;
    int _restored;
    DateTime _lastLog = DateTime.MinValue;
    DateTime _lastMissingLog = DateTime.MinValue;

    public FocusGuard(Form form)
    {
        _form = form;
        _callback = OnForeground;
    }

    /// <summary>Solo a schermo intero: in finestra il Player è una finestra normale, che si usa con mouse e tastiera.</summary>
    public bool Enabled { get; set; }

    /// <summary>Il processo della cassa (senza ".exe"); vuoto = la finestra di prima.</summary>
    public string CashApplication { get; set; } = "";

    /// <summary>Quante volte ha restituito il primo piano da quando il Player è partito.</summary>
    public int Restored => _restored;

    /// <summary>Da chiamare sul thread dell'interfaccia: l'evento arriva lì, nella coda dei messaggi.</summary>
    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;
        _previous = NativeMethods.GetForegroundWindow();
        if (IsOurs(_previous))
            _previous = IntPtr.Zero;
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        if (_hook == IntPtr.Zero)
            Log.Info("Protezione del focus della cassa non disponibile (SetWinEventHook non riuscito)");
    }

    void OnForeground(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            if (window == IntPtr.Zero)
                return;
            if (!IsOurs(window))
            {
                if (window != _previous)
                {
                    _older = _previous;
                    _previous = window;   // la cassa, o qualunque altra finestra che il cassiere usa
                }
                return;
            }
            if (!Enabled)
                return;
            var back = Target();
            if (back == IntPtr.Zero)
                return;
            if (!NativeMethods.SetForegroundWindow(back))
                return;
            _restored++;
            var now = DateTime.Now;
            if (now - _lastLog >= TimeSpan.FromMinutes(1))
            {
                _lastLog = now;
                Log.Info($"Il monitor cliente era diventato la finestra attiva (un tocco?): ridata a {CashApplications.ProcessName(back, out _) ?? "la finestra di prima"} ({_restored} volte da quando il player è aperto)");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Protezione del focus della cassa", ex);
        }
    }

    /// <summary>
    /// Dove ridare la finestra attiva: la finestra di prima se è della cassa; altrimenti la finestra più in alto della
    /// cassa (chi aveva cliccato sul desktop o sulla barra delle applicazioni non perde la battuta); senza cassa
    /// indicata, o se non è aperta, la finestra di prima.
    /// </summary>
    IntPtr Target()
    {
        var previous = Alive(_previous) ? _previous : Alive(_older) ? _older : IntPtr.Zero;
        var cash = CashApplication;
        if (string.IsNullOrEmpty(cash))
            return previous;
        if (previous != IntPtr.Zero && !NativeMethods.IsIconic(previous)
            && string.Equals(CashApplications.ProcessName(previous, out _), cash, StringComparison.OrdinalIgnoreCase))
            return previous;
        // Cercata a ogni volta: se la cassa non è aperta adesso non è un errore, e appena la aprono funziona.
        var top = CashApplications.TopWindowOf(cash);
        if (top != IntPtr.Zero)
            return top;
        var now = DateTime.Now;
        if (now - _lastMissingLog >= TimeSpan.FromMinutes(1))
        {
            _lastMissingLog = now;
            Log.Info($"L'applicazione della cassa ({cash}) non è aperta: la finestra attiva torna a quella di prima, se c'è");
        }
        return previous;
    }

    static bool Alive(IntPtr window) =>
        window != IntPtr.Zero && NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window);

    bool IsOurs(IntPtr window) =>
        window != IntPtr.Zero && _form.IsHandleCreated
        && (window == _form.Handle || NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT) == _form.Handle);

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
