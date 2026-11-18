using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MonAlbumPc.Server.Tests;

public sealed record LogLine(LogLevel Level, string Category, string Message, Exception? Exception);

/// <summary>Keeps every line logged by the server, to check that failures leave a trace.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogLine> _lines = new();

    public IReadOnlyList<LogLine> Lines => [.. _lines];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(RecordingLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            provider._lines.Enqueue(new LogLine(logLevel, category, formatter(state, exception), exception));
    }
}
