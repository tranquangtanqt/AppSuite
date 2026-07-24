using Microsoft.Extensions.Logging;

namespace Common.Logging;

/// <summary>Writes log lines to logs/{categoryPrefix}-{yyyy-MM-dd}.log under the given directory.</summary>
public sealed class RollingFileLoggerProvider(string logDirectory, string filePrefix = "app") : ILoggerProvider
{
    private readonly object _writeLock = new();

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, this);

    internal void Write(string line)
    {
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, $"{filePrefix}-{DateTime.Now:yyyy-MM-dd}.log");
        lock (_writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    public void Dispose() { }

    private sealed class RollingFileLogger(string category, RollingFileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{category}] {message}";
            if (exception is not null)
                line += Environment.NewLine + exception;

            owner.Write(line);
        }
    }
}
