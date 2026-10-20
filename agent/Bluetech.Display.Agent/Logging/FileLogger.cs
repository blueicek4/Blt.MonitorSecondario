using System.Globalization;
using System.Text;

namespace Bluetech.Display.Agent.Logging;

/// <summary>
/// Log su file, uno al giorno (log/agent-AAAAMMGG.log), tenuti per qualche giorno: per rispondere a "il monitor non si
/// aggiorna" anche senza andare sul posto.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    readonly string _folder;
    readonly object _lock = new();
    StreamWriter? _writer;
    string? _day;

    public FileLoggerProvider(string folder, int keepDays)
    {
        _folder = folder;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "agent-*.log"))
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-Math.Max(1, keepDays)))
                    File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortName(categoryName));

    public void Dispose()
    {
        lock (_lock)
            _writer?.Dispose();
    }

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = DateTime.Now;
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(Level(level)).Append(' ').Append(category).Append(": ").Append(message);
        if (exception != null)
            line.AppendLine().Append(exception);
        lock (_lock)
        {
            try
            {
                var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                if (day != _day || _writer == null)
                {
                    _writer?.Dispose();
                    _writer = new StreamWriter(new FileStream(Path.Combine(_folder, $"agent-{day}.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                    _day = day;
                }
                _writer.WriteLine(line.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Il log non deve mai fermare l'Agent.
            }
        }
    }

    static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "AVV",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    static string ShortName(string category) => category[(category.LastIndexOf('.') + 1)..];

    sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
