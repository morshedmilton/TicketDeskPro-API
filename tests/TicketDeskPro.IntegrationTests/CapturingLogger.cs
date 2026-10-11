using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TicketDeskPro.IntegrationTests;

public class CapturingLoggerProvider : ILoggerProvider
{
    public static readonly ConcurrentQueue<string> Errors = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName);
    public void Dispose() { }

    private class CapturingLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Errors.Enqueue($"[{category}] {formatter(state, exception)} {exception}");
        }
    }
}
