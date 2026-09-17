using System.Text;

namespace RepoDeck.Infrastructure;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Deliberately small. RepoDeck logs decisions and failures - API errors, downloads,
/// installs, launches - not every UI interaction.
/// </summary>
public interface IAppLog
{
    void Write(LogLevel level, string category, string message, Exception? exception = null);
}

public static class AppLogExtensions
{
    public static void Info(this IAppLog log, string category, string message) =>
        log.Write(LogLevel.Info, category, message);

    public static void Warn(this IAppLog log, string category, string message, Exception? ex = null) =>
        log.Write(LogLevel.Warning, category, message, ex);

    public static void Error(this IAppLog log, string category, string message, Exception? ex = null) =>
        log.Write(LogLevel.Error, category, message, ex);
}

/// <summary>Writes one rolling file per day, and trims files older than a fortnight.</summary>
public sealed class FileAppLog : IAppLog
{
    private readonly string _logDirectory;
    private readonly Lock _gate = new();
    private readonly LogLevel _minimumLevel;

    public FileAppLog(AppPaths paths, LogLevel minimumLevel = LogLevel.Info)
    {
        _logDirectory = paths.Logs;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(_logDirectory);
        TryTrimOldLogs();
    }

    public void Write(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (level < _minimumLevel) return;

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append(']')
            .Append(" [").Append(category).Append("] ")
            .Append(message);

        if (exception is not null)
        {
            line.Append(Environment.NewLine).Append("    ").Append(exception);
        }

        line.Append(Environment.NewLine);

        try
        {
            lock (_gate)
            {
                var path = Path.Combine(_logDirectory, $"repodeck-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line.ToString());
            }
        }
        catch
        {
            // Logging must never take the application down.
        }
    }

    private void TryTrimOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "repodeck-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}

public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();
    public void Write(LogLevel level, string category, string message, Exception? exception = null) { }
}
