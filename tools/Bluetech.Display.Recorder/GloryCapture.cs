using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Bluetech.Display.Recorder;

/// <summary>
/// Il traffico di rete tra questo PC e la cassa automatica (Glory CI-5/CI-10), registrato con pktmon, lo strumento di
/// Windows (da Windows 10 1809, niente da installare). Solo ascolto: alla cassa automatica non si manda niente. Serve
/// a vedere cosa si dicono Passepartout e la macchina durante un pagamento (le richieste SOAP e gli eventi con
/// l'importo inserito), per decidere se il monitor può mostrare il pagamento in contanti in corso.
/// Il file finisce nella registrazione come rete-glory.etl (l'originale) e rete-glory.pcapng (per Wireshark).
/// </summary>
public sealed class GloryCapture
{
    /// <summary>Esegue pktmon con quegli argomenti: codice di uscita e testo stampato (sostituibile nei test).</summary>
    public Func<string, PktmonResult> Run { get; set; } = RunPktmon;

    /// <summary>È avviato come amministratore? pktmon lo richiede (sostituibile nei test).</summary>
    public Func<bool> IsAdministrator { get; set; } = CurrentUserIsAdministrator;

    /// <summary>pktmon c'è su questo PC? (sostituibile nei test).</summary>
    public Func<bool> Available { get; set; } = () => File.Exists(PktmonPath);

    public const string EtlName = "rete-glory.etl";
    public const string PcapName = "rete-glory.pcapng";
    const string FilterName = "BluetechGlory";

    readonly RecordingSession _session;
    readonly IRecorderLog _log;
    readonly object _sync = new();
    bool _running;
    bool _modernSyntax;

    public GloryCapture(RecordingSession session, IRecorderLog log)
    {
        _session = session;
        _log = log;
    }

    public bool Running { get { lock (_sync) return _running; } }
    public string Address { get; private set; }
    /// <summary>Una riga per il riepilogo, o null se la cattura non è mai partita.</summary>
    public string Summary { get; private set; }

    static string PktmonPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "PktMon.exe");

    /// <summary>Un indirizzo IPv4 della rete locale: la cattura è solo verso la cassa automatica.</summary>
    public static bool IsLanAddress(string text, out IPAddress address)
    {
        address = null;
        if (!IPAddress.TryParse(text, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork || text.Split('.').Length != 4)
            return false;
        var b = parsed.GetAddressBytes();
        var lan = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        if (!lan)
            return false;
        address = parsed;
        return true;
    }

    /// <summary>Avvia la cattura verso quell'indirizzo; dice a video perché no, se non si può.</summary>
    public bool Start(string addressText)
    {
        lock (_sync)
        {
            if (_running)
            {
                _log.Line($"La cattura di rete verso {Address} è già attiva.", ConsoleColor.DarkYellow);
                return false;
            }
            if (!IsLanAddress(addressText, out var address))
            {
                _log.Line($"\"{addressText}\" non è l'indirizzo di un dispositivo della rete locale (es. 192.168.1.50): cattura non avviata.", ConsoleColor.DarkYellow);
                return false;
            }
            if (!Available())
            {
                Refuse("su questo Windows non c'è pktmon (arriva con Windows 10 1809): registra il traffico con Wireshark a parte");
                return false;
            }
            if (!IsAdministrator())
            {
                Refuse("pktmon chiede l'amministratore: chiudi il registratore e riaprilo con tasto destro › Esegui come amministratore");
                return false;
            }

            // Una cattura già in corso (di qualcun altro, o rimasta da un registratore chiuso male) non si tocca.
            var status = Run("status");
            if (status.ExitCode == 0 && LooksRunning(status.Output))
            {
                Refuse("pktmon sta già registrando qualcos'altro (pktmon status): fermalo con \"pktmon stop\" da amministratore e riprova");
                return false;
            }

            var help = Run("start help");
            _modernSyntax = help.Output != null && help.Output.IndexOf("--capture", StringComparison.OrdinalIgnoreCase) >= 0;
            var etl = Path.Combine(_session.SessionFolder, EtlName);
            var steps = new List<string>
            {
                "filter remove",
                $"filter add {FilterName} -i {address}",
                _modernSyntax
                    ? $"start --capture --comp nics --pkt-size 0 --file-name \"{etl}\""
                    : $"start --etw -p 0 -c 0 -f \"{etl}\"",
            };
            foreach (var step in steps)
            {
                var result = Run(step);
                if (result.ExitCode != 0)
                {
                    Run("filter remove");
                    Refuse($"pktmon {step.Split(' ')[0]} non è riuscito ({FirstLine(result.Output)})");
                    return false;
                }
            }
            _running = true;
            Address = address.ToString();
            Summary = $"{Address}, in corso";
            _session.WriteEvent(_session.NewEvent("rete-glory").With("azione", "inizio").With("indirizzo", Address).With("sintassi", _modernSyntax ? "2004+" : "1809"));
            _log.Line($"Cattura di rete verso la cassa automatica {Address} avviata (pktmon, solo ascolto).", ConsoleColor.Cyan);
            return true;
        }
    }

    /// <summary>Ferma la cattura e la converte per Wireshark.</summary>
    public void Stop()
    {
        Halt();
        Convert();
    }

    PktmonResult _stopResult;

    /// <summary>
    /// Ferma pktmon e toglie il filtro: è la parte che non deve mancare se il registratore viene chiuso (Windows
    /// concede pochi secondi), perché una cattura dimenticata resterebbe accesa. Da chiamare prima di tutto il resto.
    /// </summary>
    public void Halt()
    {
        lock (_sync)
        {
            if (!_running)
                return;
            _running = false;
            _stopResult = Run("stop");
            Run("filter remove");
        }
    }

    /// <summary>Dopo Halt: converte il file per Wireshark (il .etl originale resta comunque) e scrive il riepilogo.</summary>
    public void Convert()
    {
        lock (_sync)
        {
            var stop = _stopResult;
            if (stop == null)
                return;
            _stopResult = null;
            var etl = Path.Combine(_session.SessionFolder, EtlName);
            var pcap = Path.Combine(_session.SessionFolder, PcapName);
            var converted = false;
            if (_modernSyntax && File.Exists(etl))
            {
                converted = Run($"etl2pcap \"{etl}\" --out \"{pcap}\"").ExitCode == 0 && File.Exists(pcap);
                if (!converted)
                    converted = Run($"pcapng \"{etl}\" -o \"{pcap}\"").ExitCode == 0 && File.Exists(pcap);
            }
            var size = File.Exists(etl) ? new FileInfo(etl).Length : 0;
            Summary = stop.ExitCode != 0
                ? $"{Address}, pktmon stop non riuscito ({FirstLine(stop.Output)}): da amministratore scrivi \"pktmon stop\""
                : converted
                    ? $"{Address}, {PcapName} per Wireshark ({size / 1024} KB di cattura)"
                    : size > 0
                        ? $"{Address}, solo {EtlName} ({size / 1024} KB): si converte su un Windows più recente con \"pktmon etl2pcap\""
                        : $"{Address}, nessun file di cattura";
            _session.WriteEvent(_session.NewEvent("rete-glory").With("azione", "fine").With("indirizzo", Address)
                .With("stop", stop.ExitCode == 0).With("pcapng", converted).With("byte", size));
            _log.Line("Cattura di rete: " + Summary + ".", stop.ExitCode == 0 ? ConsoleColor.Cyan : ConsoleColor.Red);
        }
    }

    void Refuse(string why)
    {
        Summary = "non avviata: " + why;
        _session.WriteEvent(_session.NewEvent("rete-glory").With("azione", "non-avviata").With("motivo", why));
        _log.Line("Cattura di rete non avviata: " + why + ".", ConsoleColor.DarkYellow);
    }

    /// <summary>"pktmon status" a cattura spenta dice "not running" (o che non c'è niente da mostrare).</summary>
    internal static bool LooksRunning(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;
        var text = output.ToLowerInvariant();
        if (text.Contains("not running") || text.Contains("non in esecuzione") || text.Contains("is not active"))
            return false;
        return text.Contains("running") || text.Contains("collected data") || text.Contains("in esecuzione");
    }

    static string FirstLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return "nessun messaggio";
        foreach (var line in output.Split('\n'))
        {
            if (line.Trim().Length > 0)
                return line.Trim();
        }
        return "nessun messaggio";
    }

    static PktmonResult RunPktmon(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(PktmonPath, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // La conversione di una cattura lunga può volerci un po'; il resto è immediato.
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch { }
                return new PktmonResult(-1, "pktmon non ha risposto in 60 secondi");
            }
            process.WaitForExit();
            lock (output)
                return new PktmonResult(process.ExitCode, output.ToString());
        }
        catch (Exception ex)
        {
            return new PktmonResult(-1, ex.Message);
        }
    }

    static bool CurrentUserIsAdministrator()
    {
        if (!WindowsConsole.IsWindows)
            return false;
        try
        {
#if NETFRAMEWORK
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#else
            return false;
#endif
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Quello che pktmon ha risposto: codice di uscita e testo stampato.</summary>
public sealed class PktmonResult
{
    public PktmonResult(int exitCode, string output)
    {
        ExitCode = exitCode;
        Output = output ?? "";
    }

    public int ExitCode { get; }
    public string Output { get; }
}
