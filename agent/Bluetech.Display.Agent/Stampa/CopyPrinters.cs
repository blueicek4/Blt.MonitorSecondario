using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Bluetech.Display.Agent.Stampa;

/// <summary>Dove va la copia: la stampante comande in rete, la coda di stampa di Windows o, in prova, un file.</summary>
public interface ICopyPrinter
{
    string Description { get; }

    /// <param name="escPos">La copia in comandi ESC/POS.</param>
    /// <param name="text">La stessa copia come testo: la usa solo la modalità prova.</param>
    Task PrintAsync(byte[] escPos, string text, string name, CancellationToken cancel);
}

public static class CopyPrinters
{
    /// <summary>La stampante della configurazione; null se manca (allora il pulsante non c'è).</summary>
    public static ICopyPrinter? Create(PaperCopyOptions options, string logFolder)
    {
        switch (options.Collegamento.Trim().ToLowerInvariant())
        {
            case "prova":
                return new TrialPrinter(Path.Combine(logFolder, "copie"));
            case "windows":
                return string.IsNullOrWhiteSpace(options.Stampante) ? null : new WindowsPrinter(options.Stampante.Trim());
            default:
                if (string.IsNullOrWhiteSpace(options.Stampante))
                    return null;
                var address = options.Stampante.Trim();
                var colon = address.LastIndexOf(':');
                return colon > 0 && int.TryParse(address[(colon + 1)..], out var port)
                    ? new NetworkPrinter(address[..colon], port)
                    : new NetworkPrinter(address, 9100);
        }
    }
}

/// <summary>
/// ESC/POS sulla porta 9100. La stampante accetta un lavoro per volta: se sta stampando una comanda di Passepartout
/// rifiuta o fa aspettare la connessione, e l'Agent riprova finché non scade il tempo.
/// </summary>
public sealed class NetworkPrinter(string host, int port) : ICopyPrinter
{
    public string Description => $"{host}:{port}";

    public async Task PrintAsync(byte[] escPos, string text, string name, CancellationToken cancel)
    {
        while (true)
        {
            try
            {
                using var client = new TcpClient();
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(host, port, attempt.Token);
                var stream = client.GetStream();
                await stream.WriteAsync(escPos, cancel);
                await stream.FlushAsync(cancel);
                client.Client.Shutdown(SocketShutdown.Send);
                return;
            }
            catch (Exception ex) when (!cancel.IsCancellationRequested && ex is SocketException or IOException or OperationCanceledException)
            {
                await Task.Delay(300, cancel);   // occupata o non ancora raggiungibile: si riprova
            }
        }
    }
}

/// <summary>
/// ESC/POS in modalità RAW nella coda di stampa di Windows: per le stampanti USB o condivise. Windows mette in fila i
/// lavori con quelli di Passepartout. La stampante va installata per tutti gli utenti: l'Agent, da servizio, non vede
/// quelle di un solo utente.
/// </summary>
public sealed class WindowsPrinter(string queue) : ICopyPrinter
{
    public string Description => $"Windows: {queue}";

    public Task PrintAsync(byte[] escPos, string text, string name, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La coda di stampa di Windows c'è solo su Windows.");
        // Lo spooler non si può interrompere: allo scadere del tempo si risponde comunque, la copia magari esce dopo.
        return Task.Run(() => Send(queue, escPos, name), cancel).WaitAsync(cancel);
    }

    static void Send(string queue, byte[] data, string name)
    {
        if (!OpenPrinter(queue, out var printer, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Stampante \"{queue}\" non trovata");
        try
        {
            var document = new DocInfo { Name = name, DataType = "RAW" };
            if (StartDocPrinter(printer, 1, ref document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                StartPagePrinter(printer);
                if (!WritePrinter(printer, data, data.Length, out var written) || written != data.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Copia non inviata per intero");
                EndPagePrinter(printer);
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo document);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr printer, byte[] data, int count, out int written);
}

/// <summary>Modalità prova: niente stampante, la copia finisce in log\copie come testo e come comandi ESC/POS.</summary>
public sealed class TrialPrinter(string folder) : ICopyPrinter
{
    public string Description => $"prova: {folder}";

    public async Task PrintAsync(byte[] escPos, string text, string name, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}");
        await File.WriteAllTextAsync(file + ".txt", text, Encoding.UTF8, cancel);
        await File.WriteAllBytesAsync(file + ".bin", escPos, cancel);
    }
}
