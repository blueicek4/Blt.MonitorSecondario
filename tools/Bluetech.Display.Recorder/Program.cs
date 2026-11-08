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
    const string ForceWord = "forza";
    const string SkipCommand = "/salta";
    const string EndFlowCommand = "/ok";
    const string FlowsCommand = "/flussi";
    const string HelpCommand = "/aiuto";

    static RecordingSession _session;
    static readonly FlowGuide _guide = new();
    static readonly object _flowSync = new();
    static int _versionsAtFlowStart;
    static bool _flowsClosed;
    static int _finished;

    /// <summary>
    /// BluetechRecorder.exe [file o cartella] [--out cartella] [--senza-consumo]
    /// Il file viene tolto subito dopo ogni lettura, come farà il nuovo monitor (prova A/B/A del 20/10):
    /// --senza-consumo (o /consuma durante la registrazione) lo lascia dov'è, solo per le prove di diagnosi.
    /// Senza argomenti registra C:\MonitorSecondario\Scontrino.txt.
    /// Si può anche trascinare il file Scontrino.txt sull'eseguibile.
    /// I flussi di pagamento (1-6) guidano le prove una alla volta: ogni versione del file porta il numero del flusso.
    /// </summary>
    static int Main(string[] args)
    {
        WindowsConsole.Prepare();
        WindowsConsole.UseItalianInput();
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Title = "Bluetech Recorder";

        string target = null, output = null;
        var consume = true;
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                output = CleanPath(args[++i]);
            else if (string.Equals(args[i], "--senza-consumo", StringComparison.OrdinalIgnoreCase))
                consume = false;
            else if (string.Equals(args[i], "--consuma", StringComparison.OrdinalIgnoreCase))
                consume = true;   // il default; accettato per chi lo usava già
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
            log.Line($"Il file c'è già: creato {Stamp(existing.CreationTime)}, modificato {Stamp(existing.LastWriteTime)} ({Timestamps.Age(age)} fa).");
            if (existing.IsReadOnly)
            {
                log.Line("Attenzione: il file è in sola lettura, Passepartout non può sostituirlo né eliminarlo.", ConsoleColor.Magenta);
                log.Line("Togli \"Sola lettura\" dalle proprietà del file e riavvia la registrazione.", ConsoleColor.Magenta);
            }
        }
        else
        {
            log.Line("Il file non c'è ancora: aspetto che Passepartout lo scriva.");
        }
        DescribeOldMonitors(log, _session);
        DescribeDisplayAgents(log, _session);

        log.Line("");
        PrintFlows(log, withHelp: true);
        log.Line("Fuori dai flussi: scrivi una nota e premi Invio per segnare cosa stai facendo in cassa", ConsoleColor.Yellow);
        log.Line("(es. \"storno seconda riga\"). Per terminare scrivi /fine oppure Ctrl+C.", ConsoleColor.Yellow);
        log.Line("Se le operazioni in cassa non portano versioni nuove, alla seconda nota ti dico cosa provare.", ConsoleColor.Yellow);
        log.Line("");
        if (consume)
            _session.SetConsumeMode(true);
        log.Line(_session.ConsumeMode
            ? $"{ConsumeCommand} smette di togliere il file (serve solo per le prove di diagnosi) e lo riattiva."
            : $"Il file NON viene tolto dopo la lettura: {ConsumeCommand} lo fa togliere, come farà il nuovo monitor.", ConsoleColor.Yellow);
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
            {
                StepDone(log, skipped: false);   // nei flussi Invio vuol dire "passo fatto"
                continue;
            }
            if (string.Equals(line, StopCommand, StringComparison.OrdinalIgnoreCase))
                break;
            if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && PaymentFlow.Find(number) is { } flow)
            {
                BeginFlow(log, flow);
                continue;
            }
            if (string.Equals(line, SkipCommand, StringComparison.OrdinalIgnoreCase))
            {
                StepDone(log, skipped: true);
                continue;
            }
            if (string.Equals(line, EndFlowCommand, StringComparison.OrdinalIgnoreCase))
            {
                EndFlow(log);
                continue;
            }
            if (string.Equals(line, FlowsCommand, StringComparison.OrdinalIgnoreCase))
            {
                PrintFlows(log, withHelp: false);
                continue;
            }
            if (string.Equals(line, HelpCommand, StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp(log);
                continue;
            }
            if (string.Equals(line, ConsumeCommand, StringComparison.OrdinalIgnoreCase))
            {
                _session.SetConsumeMode(!_session.ConsumeMode);
                continue;
            }
            if (IsForcedConsume(line))
            {
                _session.SetConsumeMode(true, force: true);
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
        CloseFlows(log);
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

    /// <summary>
    /// Prima della sessione: il flusso in corso finisce come interrotto e il riepilogo dei flussi entra nella
    /// cartella prima dello zip. Una volta sola; chi arriva insieme (Ctrl+C e chiusura della finestra) aspetta
    /// sul lock che il primo abbia finito, così la sessione non si chiude prima.
    /// </summary>
    static void CloseFlows(ConsoleLog log)
    {
        lock (_flowSync)
        {
            if (_flowsClosed || _session == null)
                return;
            _flowsClosed = true;
            if (_guide.Current != null)
                CloseFlow(log, complete: false);
            _session.AddSummary("Flussi: " + _guide.Progress());
        }
    }

    static void PrintFlows(ConsoleLog log, bool withHelp)
    {
        log.Line("I flussi da registrare: scrivi il numero e premi Invio per cominciarne uno.", ConsoleColor.Cyan);
        foreach (var flow in PaymentFlow.All)
            log.Line($"  {flow.Number}. {flow.Name}", ConsoleColor.Cyan);
        log.Line("  Fatti: " + _guide.Progress(), ConsoleColor.DarkGray);
        if (withHelp)
            PrintHelp(log);
    }

    static void PrintHelp(ConsoleLog log)
    {
        log.Line($"Durante un flusso: Invio quando hai fatto il passo, {SkipCommand} se non si può fare, {EndFlowCommand} per chiuderlo prima.", ConsoleColor.Cyan);
        log.Line($"{FlowsCommand} rimostra l'elenco, {StopCommand} chiude la registrazione e prepara lo zip.", ConsoleColor.Cyan);
        log.Line("");
    }

    static void BeginFlow(ConsoleLog log, PaymentFlow flow)
    {
        lock (_flowSync)
        {
            if (_flowsClosed)
                return;
            if (_guide.Current != null)
                CloseFlow(log, complete: false);
            _guide.Begin(flow);
            _session.CurrentFlow = flow.Number;
            _versionsAtFlowStart = _session.Versions;
            _session.WriteEvent(_session.NewEvent("flusso").With("azione", "inizio").With("numero", flow.Number).With("nome", flow.Name));

            log.Line("");
            log.Line($"── Flusso {flow.Number} · {flow.Name} ──", ConsoleColor.Green);
            log.Line($"   Serve a vedere {flow.Why}.", ConsoleColor.DarkGray);
            ShowStep(log);
        }
    }

    static void ShowStep(ConsoleLog log)
    {
        var flow = _guide.Current;
        log.Line($"   Passo {_guide.Step + 1}/{flow.Steps.Count}: {flow.Steps[_guide.Step]}", ConsoleColor.Yellow);
        if (_guide.Step == 0)
            log.Line($"   (Invio quando l'hai fatto, {SkipCommand} se non si può, {EndFlowCommand} per chiudere il flusso.)", ConsoleColor.DarkGray);
    }

    static void StepDone(ConsoleLog log, bool skipped)
    {
        lock (_flowSync)
        {
            var flow = _guide.Current;
            if (flow == null || _flowsClosed)
            {
                if (skipped)
                    log.Line("Nessun flusso in corso: scrivi un numero da 1 a 6 per cominciarne uno.", ConsoleColor.DarkYellow);
                return;
            }
            var index = _guide.Step;
            _session.WriteEvent(_session.NewEvent("passo").With("numero", index + 1).With("testo", flow.Steps[index]).With("saltato", skipped));
            log.Line($"   {(skipped ? "– saltato" : "✓ fatto")} il passo {index + 1}", skipped ? ConsoleColor.DarkYellow : ConsoleColor.Green);
            _guide.Advance();
            if (_guide.StepsFinished)
                CloseFlow(log, complete: true);
            else
                ShowStep(log);
        }
    }

    static void EndFlow(ConsoleLog log)
    {
        lock (_flowSync)
        {
            if (_guide.Current == null)
            {
                log.Line("Nessun flusso in corso.", ConsoleColor.DarkYellow);
                return;
            }
            CloseFlow(log, complete: _guide.StepsFinished);
        }
    }

    /// <summary>Chiamato con _flowSync preso.</summary>
    static void CloseFlow(ConsoleLog log, bool complete)
    {
        var flow = _guide.End(out var finished);
        complete &= finished;
        var versions = _session.Versions - _versionsAtFlowStart;
        _session.WriteEvent(_session.NewEvent("flusso").With("azione", complete ? "fine" : "interrotto").With("numero", flow.Number)
            .With("versioni", versions));
        _session.CurrentFlow = 0;
        log.Line($"   Flusso {flow.Number} {(complete ? "completato" : "interrotto")}: {versions} {(versions == 1 ? "versione" : "versioni")} del file.",
            complete ? ConsoleColor.Green : ConsoleColor.DarkYellow);
        if (versions == 0)
            log.Line("   Nessuna versione nuova: se in cassa hai fatto i passi, Passepartout non ha scritto niente per il monitor (è un risultato anche questo).", ConsoleColor.DarkGray);
        log.Line("   Fatti: " + _guide.Progress() + ". Scrivi il numero del prossimo, o /fine.", ConsoleColor.Cyan);
        log.Line("");
    }

    /// <summary>Il Display Agent toglie lo stesso file: le versioni le prenderebbe lui.</summary>
    static void DescribeDisplayAgents(ConsoleLog log, RecordingSession session)
    {
        if (session.DisplayAgents.Count == 0)
            return;
        log.Line($"Attenzione: il Bluetech Display Agent è aperto ({string.Join(", ", session.DisplayAgents)}).", ConsoleColor.Magenta);
        log.Line("Toglie anche lui Scontrino.txt appena letto: se legge questo file, le versioni le prende lui e qui non arrivano.", ConsoleColor.Magenta);
        log.Line($"Fermalo mentre registri. Da amministratore: sc.exe stop {DisplayAgentProcess.ServiceName}  (alla fine: sc.exe start {DisplayAgentProcess.ServiceName}).", ConsoleColor.Magenta);
        log.Line("Se gira come programma e non come servizio, chiudilo. Intanto il monitor del cliente non si aggiorna.", ConsoleColor.Magenta);
    }

    /// <summary>"/consuma forza", con uno o più spazi.</summary>
    static bool IsForcedConsume(string line)
    {
        var words = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 2
            && string.Equals(words[0], ConsumeCommand, StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[1], ForceWord, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Il vecchio monitor legge Scontrino.txt nella cartella del suo exe: se è aperto, dice dove Passepartout scrive
    /// davvero (quando il display del cliente si aggiorna).
    /// </summary>
    static void DescribeOldMonitors(ConsoleLog log, RecordingSession session)
    {
        foreach (var exe in session.OldMonitors)
        {
            var folder = OldMonitor.WatchedFolder(exe);
            if (folder == null)
            {
                log.Line($"Vecchio monitor aperto ({exe}), ma Windows non dice da che cartella (gira con un altro utente?).", ConsoleColor.DarkYellow);
                log.Line("Per saperlo avvia il registratore come amministratore. Finché è aperto, /consuma resta bloccata.", ConsoleColor.DarkYellow);
            }
            else if (RecordingSession.SameFolder(folder, session.Folder))
            {
                log.Line($"Vecchio monitor aperto sullo stesso file ({exe}): chiudilo, o il file non verrà tolto dopo la lettura.", ConsoleColor.DarkYellow);
            }
            else
            {
                log.Line($"Attenzione: il vecchio monitor aperto ({exe}) legge {Path.Combine(folder, session.FileName)},", ConsoleColor.Magenta);
                log.Line("non il file che sto registrando. Se il display del cliente si aggiorna, Passepartout scrive lì:", ConsoleColor.Magenta);
                log.Line($"  BluetechRecorder.exe \"{Path.Combine(folder, session.FileName)}\"", ConsoleColor.Magenta);
            }
        }
    }

    static string Stamp(DateTime local) => local.ToString("dd/MM HH:mm:ss", CultureInfo.InvariantCulture);

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
