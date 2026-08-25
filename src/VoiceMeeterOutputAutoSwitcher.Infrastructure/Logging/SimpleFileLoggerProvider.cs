using Microsoft.Extensions.Logging;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.Logging;

public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly object _gate = new();

    public SimpleFileLoggerProvider(string? directory = null, int retentionDays = 14)
    {
        _directory = directory
                     ?? Path.Combine(
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "VoicemeeterOutputAutoSwitcher",
                         "logs");
        _retentionDays = Math.Max(1, retentionDays);
        Directory.CreateDirectory(_directory);
    }

    public string DirectoryPath => _directory;

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal void Write(string line)
    {
        lock (_gate)
        {
            var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
            CleanupOldLogs();
        }
    }

    private void CleanupOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length < 12)
                {
                    continue;
                }

                var datePart = name["app-".Length..];
                if (DateTime.TryParseExact(
                        datePart,
                        "yyyyMMdd",
                        null,
                        System.Globalization.DateTimeStyles.None,
                        out var fileDate)
                    && fileDate.Date < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class SimpleFileLogger(string categoryName, SimpleFileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {categoryName}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            provider.Write(line);
        }
    }
}
