using Common.Models;
using Microsoft.Extensions.Logging;

namespace Common.Logging;

/// <summary>
/// Raises a .NET event for every logged line instead of writing anywhere. MainLauncher's Logs page
/// subscribes to <see cref="EntryLogged"/> and appends to an ObservableCollection on the UI thread.
/// Deliberately UI-framework-free so Common stays reusable outside WinUI.
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    public event EventHandler<LogEntry>? EntryLogged;

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, this);

    internal void Raise(LogEntry entry) => EntryLogged?.Invoke(this, entry);

    public void Dispose() { }

    private sealed class InMemoryLogger(string category, InMemoryLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (exception is not null)
                message += Environment.NewLine + exception;

            owner.Raise(new LogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Level = logLevel.ToString(),
                Category = category,
                Message = message
            });
        }
    }
}
