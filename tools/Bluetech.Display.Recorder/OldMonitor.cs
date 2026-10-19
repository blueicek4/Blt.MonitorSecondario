using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Bluetech.Display.Recorder;

/// <summary>
/// Il vecchio MonitorCliente.exe aperto su questo PC. Legge sempre Scontrino.txt nella cartella del proprio exe
/// (docs/01 §2): se è aperto e il display del cliente si aggiorna, Passepartout scrive lì.
/// </summary>
public static class OldMonitor
{
    public const string ProcessName = "MonitorCliente";
    public const string ExeName = ProcessName + ".exe";

    /// <summary>Un elemento per processo: il percorso completo dell'exe, oppure solo il nome se Windows non lo dà.</summary>
    public static IReadOnlyList<string> Running()
    {
        var found = new List<string>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(ProcessName);
        }
        catch (Exception)
        {
            return found;
        }
        foreach (var process in processes)
        {
            using (process)
            {
                string path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception)
                {
                    path = null;   // processo di un altro utente, o già chiuso
                }
                found.Add(string.IsNullOrEmpty(path) ? ExeName : path);
            }
        }
        return found;
    }

    /// <summary>La cartella in cui il vecchio monitor legge Scontrino.txt, o null se il percorso dell'exe non è noto.</summary>
    public static string WatchedFolder(string exe) =>
        Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;
}
