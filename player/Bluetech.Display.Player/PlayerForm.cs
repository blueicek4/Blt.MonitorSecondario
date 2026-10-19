using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Bluetech.Display.Player;

/// <summary>
/// Finestra del monitor cliente: WebView2 a tutto schermo sullo schermo secondario.
/// Non prende mai il focus (la tastiera resta alla cassa) e non copre mai lo schermo principale in automatico.
/// </summary>
sealed class PlayerForm : Form
{
    const string HideCursorScript =
        "(function(){function add(){var s=document.createElement('style');s.textContent='*,*::before,*::after{cursor:none!important}';" +
        "(document.head||document.documentElement).appendChild(s);}" +
        "if(document.readyState==='loading'){document.addEventListener('DOMContentLoaded',add);}else{add();}})();";

    readonly bool _forceWindowed;
    readonly Panel _overlay;
    readonly Label _overlayTitle;
    readonly Label _overlayDetail;
    readonly Timer _retryTimer = new();
    readonly Timer _reloadTimer = new();
    readonly Timer _displayChangeTimer = new() { Interval = 2000 };
    readonly Timer _restartTimer = new() { Interval = 15000 };

    PlayerSettings _settings;
    WebView2 _web;
    CoreWebView2Environment _environment;
    string _hideCursorScriptId;
    bool _browserReady;
    bool _noActivate;
    bool _allowClose;
    int _retrySeconds = 5;

    public PlayerForm(PlayerSettings settings, bool forceWindowed)
    {
        _settings = settings;
        _forceWindowed = forceWindowed;
        _noActivate = settings.FullScreen && !forceWindowed;

        Text = "Bluetech Display Player";
        Icon = AppIcon.Get();
        BackColor = Color.Black;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;

        _overlayTitle = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Light", 30f),
            TextAlign = ContentAlignment.BottomCenter,
        };
        _overlayDetail = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 160,
            ForeColor = Color.FromArgb(150, 160, 175),
            Font = new Font("Segoe UI", 11f),
            TextAlign = ContentAlignment.TopCenter,
            Padding = new Padding(0, 18, 0, 0),
        };
        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            ForeColor = Color.FromArgb(110, 120, 135),
            Font = new Font("Segoe UI", 9.75f),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "powered by Bluetech",
        };
        _overlay = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 17, 23) };
        _overlay.Controls.Add(_overlayTitle);
        _overlay.Controls.Add(_overlayDetail);
        _overlay.Controls.Add(footer);
        Controls.Add(_overlay);
        ShowOverlay("Avvio in corso…", "");

        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer.Stop();
            Navigate();
        };
        _reloadTimer.Tick += (_, _) => Reload();
        _displayChangeTimer.Tick += (_, _) =>
        {
            _displayChangeTimer.Stop();
            ApplyPlacement();
        };
        _restartTimer.Tick += async (_, _) =>
        {
            _restartTimer.Stop();
            await StartBrowserAsync();
        };

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_noActivate)
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void AllowClose() => _allowClose = true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 e simili non chiudono il monitor cliente: si esce solo dal menu, con il codice.
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _retryTimer.Dispose();
            _reloadTimer.Dispose();
            _displayChangeTimer.Dispose();
            _restartTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    public async void ApplySettings(PlayerSettings settings)
    {
        var urlChanged = settings.Url != _settings.Url;
        var cursorChanged = settings.HideCursor != _settings.HideCursor;
        _settings = settings;

        ApplyPlacement();
        if (!_browserReady)
            return;

        _web.ZoomFactor = settings.ZoomPercent / 100.0;
        ConfigureReloadTimer();
        try
        {
            if (cursorChanged)
                await UpdateCursorScriptAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Aggiornamento puntatore non riuscito", ex);
        }
        if (urlChanged || cursorChanged)
            Navigate();
    }

    /// <summary>Ricarica l'indirizzo configurato (anche dopo un errore di connessione).</summary>
    public void ReloadPage()
    {
        _retryTimer.Stop();
        Navigate();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_DPICHANGED = 0x02E0;
        base.WndProc(ref m);
        // Su monitor con scala diversa Windows ridimensiona la finestra: la riportiamo esattamente sullo schermo.
        if (m.Msg == WM_DPICHANGED && FormBorderStyle == FormBorderStyle.None)
            BeginInvoke((Action)ApplyPlacement);
    }

    /// <summary>Posiziona la finestra sullo schermo giusto, oppure la nasconde se quello schermo non c'è.</summary>
    public void ApplyPlacement()
    {
        var target = ScreenLocator.Find(_settings);
        if (_forceWindowed || !_settings.FullScreen)
        {
            ShowWindowed(target ?? Screen.PrimaryScreen);
            return;
        }

        if (target != null)
        {
            ShowFullScreen(target);
            return;
        }

        if (_settings.WhenScreenMissing == MissingScreenBehavior.WindowedOnPrimary)
        {
            ShowWindowed(Screen.PrimaryScreen);
            return;
        }

        if (Visible)
        {
            Log.Info("Schermo secondario assente: player nascosto");
            Hide();
        }
    }

    void ShowFullScreen(Screen screen)
    {
        SetNoActivate(true);
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        var b = screen.Bounds;
        if (!Visible)
        {
            Bounds = b;
            Show();
            Log.Info($"Player a schermo intero su {screen.DeviceName} {b.Width}×{b.Height}");
        }
        NativeMethods.SetWindowPos(Handle, _settings.TopMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    void ShowWindowed(Screen screen)
    {
        SetNoActivate(false);
        FormBorderStyle = FormBorderStyle.Sizable;
        var area = screen.WorkingArea;
        var size = new Size(area.Width * 2 / 3, area.Height * 2 / 3);
        var bounds = new Rectangle(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height);
        if (!Visible || !screen.Bounds.Contains(Bounds.Location))
            Bounds = bounds;
        if (!Visible)
            Show();
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    void SetNoActivate(bool enabled)
    {
        _noActivate = enabled;
        if (!IsHandleCreated)
            return;
        var style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
        var updated = enabled ? style | NativeMethods.WS_EX_NOACTIVATE : style & ~(long)NativeMethods.WS_EX_NOACTIVATE;
        if (updated != style)
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, updated);
    }

    void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        // Gli schermi si "assestano" dopo l'evento: ricontrolliamo con un attimo di ritardo, sul thread UI.
        if (IsHandleCreated)
            BeginInvoke((Action)(() => { _displayChangeTimer.Stop(); _displayChangeTimer.Start(); }));
        else
            _displayChangeTimer.Start();
    }

    void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            OnDisplaySettingsChanged(sender, e);
    }

    /// <summary>Crea il browser. Se la finestra è nascosta (schermo assente) l'inizializzazione si completa quando compare.</summary>
    public async Task StartBrowserAsync()
    {
        try
        {
            if (_environment == null)
            {
                var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bluetech", "DisplayPlayer", "WebView2");
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                _environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
                Log.Info("WebView2 runtime " + _environment.BrowserVersionString);
            }

            _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
            Controls.Add(_web);
            _overlay.BringToFront();
            await _web.EnsureCoreWebView2Async(_environment);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Log.Error("WebView2 Runtime non installato", ex);
            ShowOverlay("Componente mancante", "Installa Microsoft Edge WebView2 Runtime (https://go.microsoft.com/fwlink/p/?LinkId=2124703) e riavvia il player.");
            return;
        }
        catch (Exception ex)
        {
            // Es. cartella dati ancora occupata dal browser precedente: si riprova tra poco.
            Log.Error("Avvio del browser non riuscito, nuovo tentativo tra 15 s", ex);
            ShowOverlay("Avvio della visualizzazione…", ex.Message);
            DisposeBrowser();
            _environment = null;
            _restartTimer.Start();
            return;
        }

        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        TrySet(() => s.IsPinchZoomEnabled = false);
        TrySet(() => s.IsSwipeNavigationEnabled = false);
        TrySet(() => s.IsGeneralAutofillEnabled = false);
        TrySet(() => s.IsPasswordAutosaveEnabled = false);

        core.NewWindowRequested += (_, e) => e.Handled = true;   // niente popup sul monitor cliente
        core.NavigationCompleted += OnNavigationCompleted;
        core.ProcessFailed += OnProcessFailed;

        await UpdateCursorScriptAsync();
        _web.ZoomFactor = _settings.ZoomPercent / 100.0;
        _browserReady = true;
        ConfigureReloadTimer();
        Navigate();
    }

    async Task UpdateCursorScriptAsync()
    {
        var core = _web?.CoreWebView2;
        if (core == null)
            return;

        if (_hideCursorScriptId != null)
        {
            core.RemoveScriptToExecuteOnDocumentCreated(_hideCursorScriptId);
            _hideCursorScriptId = null;
        }
        if (_settings.HideCursor)
            _hideCursorScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(HideCursorScript);
    }

    void Navigate()
    {
        if (!_browserReady)
            return;
        try
        {
            _web.CoreWebView2.Navigate(_settings.Url);
        }
        catch (ArgumentException ex)
        {
            Log.Error("Indirizzo non valido: " + _settings.Url, ex);
            ShowOverlay("Indirizzo non valido", _settings.Url);
        }
    }

    void Reload()
    {
        if (_browserReady)
            _web.CoreWebView2.Reload();
    }

    void ConfigureReloadTimer()
    {
        _reloadTimer.Stop();
        if (_settings.ReloadMinutes <= 0)
            return;
        _reloadTimer.Interval = _settings.ReloadMinutes * 60_000;
        _reloadTimer.Start();
    }

    void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _retrySeconds = 5;
            HideOverlay();
            return;
        }

        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            return;

        // Monitor non raggiungibile: copriamo la pagina d'errore del browser e riproviamo con attesa crescente.
        Log.Info($"Navigazione non riuscita ({e.WebErrorStatus}), nuovo tentativo tra {_retrySeconds} s");
        ShowOverlay("Connessione al monitor in corso…", $"{_settings.Url}\n{e.WebErrorStatus} — nuovo tentativo tra {_retrySeconds} s");
        _retryTimer.Stop();
        _retryTimer.Interval = _retrySeconds * 1000;
        _retryTimer.Start();
        _retrySeconds = Math.Min(_retrySeconds * 2, 60);
    }

    void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Error("Processo del browser terminato: " + e.ProcessFailedKind);
        ShowOverlay("Riavvio della visualizzazione…", "");

        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // Il browser è perso: si ricrea il controllo da zero.
            BeginInvoke((Action)(() =>
            {
                DisposeBrowser();
                _environment = null;
                _restartTimer.Stop();
                _restartTimer.Start();
            }));
        }
        else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            BeginInvoke((Action)Reload);
        }
    }

    void DisposeBrowser()
    {
        _browserReady = false;
        _reloadTimer.Stop();
        _retryTimer.Stop();
        _hideCursorScriptId = null;
        if (_web == null)
            return;
        Controls.Remove(_web);
        _web.Dispose();
        _web = null;
    }

    void ShowOverlay(string title, string detail)
    {
        _overlayTitle.Text = title;
        _overlayDetail.Text = detail;
        _overlay.Visible = true;
        _overlay.BringToFront();
    }

    void HideOverlay() => _overlay.Visible = false;

    static void TrySet(Action action)
    {
        // Proprietà disponibili solo con runtime WebView2 recenti: se mancano, si prosegue.
        try { action(); }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException) { }
    }
}
