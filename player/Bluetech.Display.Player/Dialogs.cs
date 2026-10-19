using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

/// <summary>Impostazioni comuni dei dialoghi: sullo schermo principale (dove lavora il tecnico) e sopra al player.</summary>
abstract class DialogBase : Form
{
    protected DialogBase()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9.75f);
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }

    protected static Label Caption(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 12, 6),
        Font = bold ? new Font("Segoe UI Semibold", 10.5f) : null,
    };

    protected static Label ErrorLabel() => new()
    {
        AutoSize = true,
        ForeColor = Color.Firebrick,
        MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 8, 0, 0),
    };
}

sealed class PinForm : DialogBase
{
    readonly TextBox _pin = new() { UseSystemPasswordChar = true, Width = 240, MaxLength = PinProtector.MaxLength };
    readonly Label _error = ErrorLabel();
    readonly PlayerSettings _settings;

    public PinForm(string reason, PlayerSettings settings)
    {
        _settings = settings;
        Text = "Bluetech Display Player";

        var ok = new Button { Text = "Conferma", DialogResult = DialogResult.None, AutoSize = true };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Check();
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(Caption(reason, bold: true));
        layout.Controls.Add(Caption("Codice di accesso"));
        layout.Controls.Add(_pin);
        layout.Controls.Add(_error);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        ShowLockout();
    }

    void Check()
    {
        if (ShowLockout())
            return;

        if (PinProtector.Verify(_settings, _pin.Text))
        {
            DialogResult = DialogResult.OK;
            return;
        }

        _pin.Clear();
        _pin.Focus();
        if (!ShowLockout())
            _error.Text = "Codice errato.";
        Log.Info("Codice di accesso errato");
    }

    bool ShowLockout()
    {
        var remaining = PinProtector.LockoutRemaining;
        if (remaining <= TimeSpan.Zero)
            return false;
        _error.Text = $"Troppi tentativi errati. Riprova tra {Math.Ceiling(remaining.TotalSeconds)} secondi.";
        return true;
    }
}

/// <summary>Configurazione del player. In modalità "prima installazione" il codice di accesso è obbligatorio.</summary>
sealed class ConfigForm : DialogBase
{
    readonly bool _firstRun;
    readonly PlayerSettings _original;

    readonly TextBox _url = new() { Width = 420 };
    readonly ComboBox _screen = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly ComboBox _missing = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    readonly CheckBox _fullScreen = Check("Schermo intero senza bordi");
    readonly CheckBox _topMost = Check("Sempre in primo piano");
    readonly CheckBox _hideCursor = Check("Nascondi il puntatore del mouse");
    readonly CheckBox _preventSleep = Check("Impedisci lo spegnimento dello schermo");
    readonly CheckBox _autoStart = Check("Avvia automaticamente all'accesso a Windows");
    readonly NumericUpDown _zoom = new() { Minimum = 25, Maximum = 300, Increment = 5, Width = 80 };
    readonly NumericUpDown _reload = new() { Minimum = 0, Maximum = 1440, Width = 80 };
    readonly TextBox _pin = new() { UseSystemPasswordChar = true, Width = 240, MaxLength = PinProtector.MaxLength };
    readonly TextBox _pinConfirm = new() { UseSystemPasswordChar = true, Width = 240, MaxLength = PinProtector.MaxLength };
    readonly Label _error = ErrorLabel();

    public PlayerSettings Result { get; private set; }

    public ConfigForm(PlayerSettings current, bool firstRun)
    {
        _firstRun = firstRun;
        _original = current;
        Text = firstRun ? "Bluetech Display Player — prima configurazione" : "Bluetech Display Player — configurazione";

        _missing.Items.AddRange(new object[]
        {
            "Nascondi il player finché lo schermo non torna (consigliato)",
            "Mostralo in una finestra sullo schermo principale",
        });

        var identify = new Button { Text = "Identifica schermi", AutoSize = true };
        identify.Click += (_, _) => ScreenIdentifier.Show();

        var screenRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        screenRow.Controls.AddRange(new Control[] { _screen, identify });

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string caption, Control control)
        {
            grid.Controls.Add(Caption(caption));
            grid.Controls.Add(control);
        }

        void Section(string title)
        {
            var label = Caption(title, bold: true);
            label.Margin = new Padding(0, 14, 0, 4);
            grid.Controls.Add(label);
            grid.SetColumnSpan(label, 2);
        }

        void Wide(Control control)
        {
            grid.Controls.Add(new Label { AutoSize = true });
            grid.Controls.Add(control);
        }

        Section("Monitor cliente");
        Row("Indirizzo web", _url);
        Row("Schermo", screenRow);
        Row("Se lo schermo non c'è", _missing);
        Wide(_fullScreen);
        Wide(_topMost);
        Wide(_hideCursor);
        Wide(_preventSleep);
        Wide(_autoStart);
        Row("Zoom pagina (%)", _zoom);
        Row("Ricarica ogni (minuti, 0 = mai)", _reload);

        Section(firstRun ? "Codice di accesso (obbligatorio)" : "Cambia codice di accesso (lascia vuoto per non cambiarlo)");
        Row("Nuovo codice", _pin);
        Row("Conferma codice", _pinConfirm);

        var hint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 14, 0, 0),
            Text = "Configurazione: icona Bluetech nell'area di notifica oppure Ctrl+Alt+Maiusc+F12.\n" +
                   "File: " + PlayerSettings.FilePath + "\n" +
                   "Codice dimenticato: cancella il file e riavvia il player per ripetere la prima configurazione.",
        };
        grid.Controls.Add(hint);
        grid.SetColumnSpan(hint, 2);
        grid.Controls.Add(_error);
        grid.SetColumnSpan(_error, 2);

        var save = new Button { Text = "Salva", AutoSize = true };
        var cancel = new Button { Text = firstRun ? "Esci" : "Annulla", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => TrySave();
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, save });
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        LoadValues(current);
    }

    static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };

    void LoadValues(PlayerSettings s)
    {
        _url.Text = s.Url;

        _screen.Items.Add(new ScreenItem("", "Automatico — primo schermo secondario"));
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
            _screen.Items.Add(new ScreenItem(screens[i].DeviceName, ScreenLocator.Describe(screens[i], i)));
        if (!string.IsNullOrEmpty(s.Screen) && !screens.Any(x => x.DeviceName == s.Screen))
            _screen.Items.Add(new ScreenItem(s.Screen, s.Screen + " (non collegato)"));
        _screen.SelectedItem = _screen.Items.Cast<ScreenItem>().First(i => i.DeviceName == (s.Screen ?? ""));

        _missing.SelectedIndex = s.WhenScreenMissing == MissingScreenBehavior.WindowedOnPrimary ? 1 : 0;
        _fullScreen.Checked = s.FullScreen;
        _topMost.Checked = s.TopMost;
        _hideCursor.Checked = s.HideCursor;
        _preventSleep.Checked = s.PreventSleep;
        _autoStart.Checked = _firstRun || AutoStart.IsEnabled;
        _zoom.Value = Math.Max(_zoom.Minimum, Math.Min(_zoom.Maximum, s.ZoomPercent));
        _reload.Value = Math.Max(_reload.Minimum, Math.Min(_reload.Maximum, s.ReloadMinutes));
    }

    void TrySave()
    {
        _error.Text = "";

        if (!PlayerSettings.TryNormalizeUrl(_url.Text, out var url))
        {
            _error.Text = "Indirizzo web non valido. Esempio: http://192.168.1.20:5080/display";
            _url.Focus();
            return;
        }

        var pinRequested = _firstRun || _pin.Text.Length > 0 || _pinConfirm.Text.Length > 0;
        if (pinRequested)
        {
            if (!PinProtector.IsValidNewPin(_pin.Text))
            {
                _error.Text = $"Il codice deve avere almeno {PinProtector.MinLength} caratteri.";
                _pin.Focus();
                return;
            }
            if (_pin.Text != _pinConfirm.Text)
            {
                _error.Text = "I due codici non coincidono.";
                _pinConfirm.Focus();
                return;
            }
        }

        var result = _original.Clone();
        result.Url = url;
        result.Screen = ((ScreenItem)_screen.SelectedItem).DeviceName;
        result.WhenScreenMissing = _missing.SelectedIndex == 1 ? MissingScreenBehavior.WindowedOnPrimary : MissingScreenBehavior.Hide;
        result.FullScreen = _fullScreen.Checked;
        result.TopMost = _topMost.Checked;
        result.HideCursor = _hideCursor.Checked;
        result.PreventSleep = _preventSleep.Checked;
        result.ZoomPercent = (int)_zoom.Value;
        result.ReloadMinutes = (int)_reload.Value;
        if (pinRequested)
            PinProtector.SetPin(result, _pin.Text);

        try
        {
            result.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Salvataggio configurazione non riuscito", ex);
            _error.Text = "Impossibile salvare la configurazione: " + ex.Message;
            return;
        }

        try
        {
            AutoStart.Set(_autoStart.Checked);
        }
        catch (Exception ex)
        {
            Log.Error("Impostazione avvio automatico non riuscita", ex);
            MessageBox.Show(this, "Configurazione salvata, ma non è stato possibile impostare l'avvio automatico:\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Log.Info($"Configurazione salvata: url={result.Url} schermo={(result.Screen.Length == 0 ? "auto" : result.Screen)} fullscreen={result.FullScreen}");
        Result = result;
        DialogResult = DialogResult.OK;
    }

    sealed class ScreenItem
    {
        public ScreenItem(string deviceName, string text)
        {
            DeviceName = deviceName;
            Text = text;
        }

        public string DeviceName { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }
}

/// <summary>Mostra per qualche secondo un numero grande su ogni schermo, come le impostazioni di Windows.</summary>
static class ScreenIdentifier
{
    public static void Show()
    {
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var form = new NoActivateForm
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(37, 99, 235),
                Size = new Size(320, 220),
            };
            form.Location = new Point(screen.Bounds.Left + (screen.Bounds.Width - form.Width) / 2, screen.Bounds.Top + (screen.Bounds.Height - form.Height) / 2);
            form.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                Text = $"{i + 1}\n{screen.Bounds.Width}×{screen.Bounds.Height}",
            });

            var timer = new Timer { Interval = 3000 };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                form.Close();
            };
            form.FormClosed += (_, _) => form.Dispose();
            form.Show();
            timer.Start();
        }
    }

    sealed class NoActivateForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }
}
