using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Middt.Framework.Tests.Infrastructure;

public sealed record CapturedLog(string Category, LogLevel Level, EventId EventId, string Message);

/// <summary>Records every log entry so tests can assert on security relevant logging.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, eventId, formatter(state, exception)));
    }
}
