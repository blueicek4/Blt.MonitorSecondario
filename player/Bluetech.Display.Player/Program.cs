using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Bluetech.Display.Player;

static class Program
{
    /// <summary>
    /// Opzioni da riga di comando:
    ///   --config    apre subito la configurazione (dopo il codice)
    ///   --windowed  forza la finestra normale, per prove su PC con un solo monitor
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        var openConfig = args.Any(a => string.Equals(a, "--config", StringComparison.OrdinalIgnoreCase));
        var windowed = args.Any(a => string.Equals(a, "--windowed", StringComparison.OrdinalIgnoreCase));

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(true, @"Local\Bluetech.Display.Player", out var firstInstance);
        if (!firstInstance)
        {
            if (openConfig)
                MessageBox.Show("Il player è già in esecuzione.\nPer configurarlo usa l'icona Bluetech nell'area di notifica oppure Ctrl+Alt+Maiusc+F12.",
                    "Bluetech Display Player", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.ThreadException += (_, e) => Log.Error("Errore non gestito", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Errore fatale", e.ExceptionObject as Exception);
        Log.Cleanup();
        Log.Info("Avvio " + Application.ProductVersion + (windowed ? " (finestra)" : ""));

        var settings = PlayerSettings.Load();
        if (!settings.IsConfigured)
        {
            using var setup = new ConfigForm(settings, firstRun: true);
            if (setup.ShowDialog() != DialogResult.OK)
                return 0;
            settings = setup.Result;
            openConfig = false;
        }

        Application.Run(new PlayerContext(settings, openConfig, windowed));
        return 0;
    }
}
