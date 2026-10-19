using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Bluetech.Display.Recorder;

static class Program
{
    const string DefaultTarget = @"C:\MonitorSecondario\Scontrino.txt";
    const string StopCommand = "/fine";

    static RecordingSession _session;
    static int _finished;

    /// <summary>
    /// BluetechRecorder.exe [file o cartella] [--out cartella]
    /// Senza argomenti registra C:\MonitorSecondario\Scontrino.txt.
    /// Si può anche trascinare il file Scontrino.txt sull'eseguibile.
    /// </summary>
    static int Main(string[] args)
    {
        WindowsConsole.Prepare();
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Title = "Bluetech Recorder";

        string target = null, output = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                output = args[++i];
            else
                target ??= args[i];
        }
        target ??= DefaultTarget;
        if (Directory.Exists(target))
            target = Path.Combine(target, "Scontrino.txt");

        var log = new ConsoleLog();
        log.Line("Bluetech Display Recorder " + typeof(Program).Assembly.GetName().Version, ConsoleColor.Cyan);

        var folder = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!Directory.Exists(folder))
        {
            log.Line($"La cartella {folder} non esiste. Indica il percorso giusto, per esempio:", ConsoleColor.Red);
            log.Line(@"  BluetechRecorder.exe D:\Passepartout\Monitor\Scontrino.txt");
            WaitForEnter();
            return 1;
        }

        output ??= ChooseOutputRoot(folder);
        _session = new RecordingSession(target, output, log);
        try
        {
            _session.Start(MachineInfo.Describe());
        }
        catch (Exception ex)
        {
            log.Line("Impossibile avviare la registrazione: " + ex.Message, ConsoleColor.Red);
            WaitForEnter();
            return 1;
        }

        log.Line("Registro:     " + _session.TargetPath);
        log.Line("Salvo in:     " + _session.SessionFolder);
        log.Line("");
        log.Line("Scrivi una nota e premi Invio per segnare cosa stai facendo in cassa", ConsoleColor.Yellow);
        log.Line("(es. \"storno seconda riga\", \"pago con Satispay\"). Per terminare scrivi /fine oppure Ctrl+C.", ConsoleColor.Yellow);
        log.Line("");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Finish(log);
        };
        WindowsConsole.OnClose(() => Finish(log));

        while (true)
        {
            var line = Console.ReadLine();
            if (line == null || _session.IsStopped)
                break;
            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (string.Equals(line, StopCommand, StringComparison.OrdinalIgnoreCase))
                break;
            _session.AddNote(line);
        }

        Finish(log);
        WaitForEnter();
        return 0;
    }

    /// <summary>Chiusura unica, qualunque sia la strada (comando, Ctrl+C, chiusura della finestra).</summary>
    static void Finish(ConsoleLog log)
    {
        _session.Stop();
        if (Interlocked.Exchange(ref _finished, 1) == 1)
            return;

        log.Line("");
        if (_session.ZipPath != null)
        {
            log.Line("Registrazione completata. File da inviare:", ConsoleColor.Green);
            log.Line("  " + _session.ZipPath, ConsoleColor.Green);
        }
        else
        {
            log.Line("Registrazione completata. Cartella da inviare:", ConsoleColor.Green);
            log.Line("  " + _session.SessionFolder, ConsoleColor.Green);
        }
        log.Line("Premi Invio per chiudere.");
    }

    /// <summary>Accanto al file registrato; se la cartella non è scrivibile, sul Desktop.</summary>
    static string ChooseOutputRoot(string folder)
    {
        var preferred = Path.Combine(folder, "Registrazioni");
        try
        {
            Directory.CreateDirectory(preferred);
            var probe = Path.Combine(preferred, ".prova-scrittura");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return preferred;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Registrazioni Monitor");
        }
    }

    static void WaitForEnter()
    {
        if (!Console.IsInputRedirected)
            Console.ReadLine();
    }
}

sealed class ConsoleLog : IRecorderLog
{
    readonly object _sync = new();

    public void Line(string text, ConsoleColor? color = null)
    {
        lock (_sync)
        {
            if (color.HasValue)
                Console.ForegroundColor = color.Value;
            Console.WriteLine(text);
            if (color.HasValue)
                Console.ResetColor();
        }
    }
}
