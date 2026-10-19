using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
                var path = ImagePath(process);
                found.Add(string.IsNullOrEmpty(path) ? ExeName : path);
            }
        }
        return found;
    }

    /// <summary>La cartella in cui il vecchio monitor legge Scontrino.txt, o null se il percorso dell'exe non è noto.</summary>
    public static string WatchedFolder(string exe) =>
        Path.IsPathRooted(exe) ? Path.GetDirectoryName(exe) : null;

    /// <summary>
    /// Percorso dell'exe. Prima con il permesso minimo (PROCESS_QUERY_LIMITED_INFORMATION), che Windows concede anche
    /// per un processo avviato come amministratore: Process.MainModule in quel caso fallisce. Resta ignoto per un
    /// processo di un altro utente, se il registratore non è avviato come amministratore.
    /// </summary>
    static string ImagePath(Process process)
    {
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            try
            {
                var handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        var size = 1024;
                        var name = new StringBuilder(size);
                        if (QueryFullProcessImageName(handle, 0, name, ref size))
                            return name.ToString(0, size);
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
            catch (Exception)
            {
                // si prova con MainModule
            }
        }
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception)
        {
            return null;   // processo di un altro utente, o già chiuso
        }
    }

    const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
