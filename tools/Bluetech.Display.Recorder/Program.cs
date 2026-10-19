using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Bluetech.Display.Recorder;

static class Program
{
    const string DefaultTarget = @"C:\MonitorSecondario\Scontrino.txt";
    const string StopCommand = "/fine";
    const string ConsumeCommand = "/consuma";

    static RecordingSession _session;
    static int _finished;

    /// <summary>
    /// BluetechRecorder.exe [file o cartella] [--out cartella] [--consuma]
    /// Senza argomenti registra C:\MonitorSecondario\Scontrino.txt.
    /// Si può anche trascinare il file Scontrino.txt sull'eseguibile.
    /// </summary>
    static int Main(string[] args)
    {
        WindowsConsole.Prepare();
        WindowsConsole.UseItalianInput();
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Title = "Bluetech Recorder";

        string target = null, output = null;
        var consume = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                output = CleanPath(args[++i]);
            else if (string.Equals(args[i], "--consuma", StringComparison.OrdinalIgnoreCase))
                consume = true;
            else
                target ??= CleanPath(args[i]);
        }
        target ??= DefaultTarget;

        var log = new ConsoleLog();
        log.Line("Bluetech Display Recorder " + typeof(Program).Assembly.GetName().Version, ConsoleColor.Cyan);

        string folder;
        try
        {
            if (Directory.Exists(target))
                target = Path.Combine(target, "Scontrino.txt");
            target = Path.GetFullPath(target);
            folder = Path.GetDirectoryName(target);
            if (output != null)
                output = Path.GetFullPath(output);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            log.Line($"Percorso non valido: {target} ({ex.Message})", ConsoleColor.Red);
            log.Flush();
            WaitForEnter();
            return 1;
        }

        if (!Directory.Exists(folder))
        {
            log.Line($"La cartella {folder} non esiste. Indica il percorso giusto, per esempio:", ConsoleColor.Red);
            log.Line(@"  BluetechRecorder.exe D:\Passepartout\Monitor\Scontrino.txt");
            log.Flush();
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
            log.Flush();
            WaitForEnter();
            return 1;
        }

        log.Line("Registro:     " + _session.TargetPath);
        log.Line("Salvo in:     " + _session.SessionFolder);
        var existing = new FileInfo(_session.TargetPath);
        if (existing.Exists)
        {
            var age = DateTime.Now - existing.LastWriteTime;
            log.Line($"Il file c'è già: ultima modifica {existing.LastWriteTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} ({Describe(age)} fa).");
            if (!consume)
            {
                log.Line("Se Passepartout ha SovrascriviSeEsistente=false non lo riscriverà finché non viene tolto:", ConsoleColor.DarkYellow);
                log.Line($"in quel caso scrivi {ConsumeCommand} (il registratore lo toglierà dopo ogni lettura).", ConsoleColor.DarkYellow);
            }
        }
        else
        {
            log.Line("Il file non c'è ancora: aspetto che Passepartout lo scriva.");
        }
        log.Line("");
        log.Line("Scrivi una nota e premi Invio per segnare cosa stai facendo in cassa", ConsoleColor.Yellow);
        log.Line("(es. \"storno seconda riga\", \"pago con Satispay\"). Per terminare scrivi /fine oppure Ctrl+C.", ConsoleColor.Yellow);
        log.Line($"{ConsumeCommand} attiva o disattiva la modalità consumo.", ConsoleColor.Yellow);
        log.Line("");
        if (consume)
            _session.SetConsumeMode(true);

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
            if (string.Equals(line, ConsumeCommand, StringComparison.OrdinalIgnoreCase))
            {
                _session.SetConsumeMode(!_session.ConsumeMode);
                continue;
            }
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
        log.Flush();
    }

    /// <summary>"C:\cartella\" tra virgolette arriva come C:\cartella" (la barra finale protegge la virgoletta).</summary>
    static string CleanPath(string arg) => arg.Trim().Trim('"').Trim();

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

    static string Describe(TimeSpan age)
    {
        if (age.TotalSeconds < 90)
            return $"{Math.Max(0, (int)age.TotalSeconds)} s";
        if (age.TotalMinutes < 90)
            return $"{(int)age.TotalMinutes} min";
        if (age.TotalHours < 48)
            return $"{(int)age.TotalHours} ore";
        return $"{(int)age.TotalDays} giorni";
    }

    static void WaitForEnter()
    {
        if (!Console.IsInputRedirected)
            Console.ReadLine();
    }
}

/// <summary>
/// Scrive a video su un thread dedicato: se la console si ferma (finestra in pausa, selezione, scorrimento),
/// la registrazione continua comunque.
/// </summary>
sealed class ConsoleLog : IRecorderLog
{
    readonly BlockingCollection<Entry> _queue = new();

    public ConsoleLog()
    {
        new Thread(Run) { IsBackground = true, Name = "Console" }.Start();
    }

    public void Line(string text, ConsoleColor? color = null) => _queue.Add(new Entry { Text = text, Color = color });

    /// <summary>Aspetta che tutto quanto accodato sia a video (al massimo 3 secondi).</summary>
    public void Flush()
    {
        using var done = new ManualResetEventSlim(false);
        _queue.Add(new Entry { Done = done });
        done.Wait(TimeSpan.FromSeconds(3));
    }

    void Run()
    {
        foreach (var entry in _queue.GetConsumingEnumerable())
        {
            if (entry.Done != null)
            {
                entry.Done.Set();
                continue;
            }
            try
            {
                if (entry.Color.HasValue)
                    Console.ForegroundColor = entry.Color.Value;
                Console.WriteLine(entry.Text);
                if (entry.Color.HasValue)
                    Console.ResetColor();
            }
            catch (IOException) { }
        }
    }

    sealed class Entry
    {
        public string Text;
        public ConsoleColor? Color;
        public ManualResetEventSlim Done;
    }
}
