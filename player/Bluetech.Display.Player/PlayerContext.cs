using System;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

/// <summary>
/// Ciclo di vita del player: finestra del monitor, icona nell'area di notifica e scorciatoia globale.
/// La finestra può restare nascosta (schermo secondario assente) mentre il player continua a girare.
/// </summary>
sealed class PlayerContext : ApplicationContext
{
    const Keys ConfigKey = Keys.F12;
    const uint ConfigModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;

    readonly PlayerForm _form;
    readonly NotifyIcon _tray;
    readonly HotkeyWindow _hotkey = new();
    PlayerSettings _settings;
    bool _dialogOpen;

    public PlayerContext(PlayerSettings settings, bool openConfig, bool forceWindowed)
    {
        _settings = settings;
        NativeMethods.PreventSleep(settings.PreventSleep);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Configurazione…", null, (_, _) => OpenConfiguration());
        menu.Items.Add("Ricarica la pagina", null, (_, _) => _form.ReloadPage());
        menu.Items.Add("Identifica schermi", null, (_, _) => ScreenIdentifier.Show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci…", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = "Bluetech Display Player",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenConfiguration();

        _hotkey.Pressed += (_, _) => OpenConfiguration();
        if (!_hotkey.Register(ConfigModifiers, ConfigKey))
            Log.Info("Scorciatoia Ctrl+Alt+Maiusc+F12 già usata da un altro programma: usare l'icona nell'area di notifica");

        _form = new PlayerForm(settings, forceWindowed);
        _form.CloseRequested += Exit;
        _form.ApplyPlacement();
        _ = _form.StartBrowserAsync();

        if (openConfig)
        {
            var once = new Timer { Interval = 300 };
            once.Tick += (_, _) =>
            {
                once.Dispose();
                OpenConfiguration();
            };
            once.Start();
        }
    }

    void OpenConfiguration()
    {
        if (_dialogOpen)
            return;
        _dialogOpen = true;
        try
        {
            if (!PinProtector.Prompt("Accesso alla configurazione", _settings))
                return;

            using var form = new ConfigForm(_settings, firstRun: false);
            if (form.ShowDialog() != DialogResult.OK)
                return;

            _settings = form.Result;
            NativeMethods.PreventSleep(_settings.PreventSleep);
            _form.ApplySettings(_settings);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    void Exit()
    {
        if (_dialogOpen)
            return;
        _dialogOpen = true;
        try
        {
            if (!PinProtector.Prompt("Chiusura del monitor cliente", _settings))
                return;
        }
        finally
        {
            _dialogOpen = false;
        }

        Log.Info("Player chiuso dall'operatore");
        _form.AllowClose();
        _form.Close();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _hotkey.Dispose();
        NativeMethods.PreventSleep(false);
        _form.Dispose();
        base.ExitThreadCore();
    }
}
