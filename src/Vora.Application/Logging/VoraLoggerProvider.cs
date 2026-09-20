using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Logging;

[ProviderAlias("Vora")]
public sealed class VoraLoggerProvider : ILoggerProvider
{
    private readonly ILogBuffer _buffer;
    private readonly LogFileSink _fileSink;
    private readonly LogLevelOverrideProvider _levels;
    private readonly ConcurrentDictionary<string, VoraLogger> _loggers = new(StringComparer.Ordinal);

    public VoraLoggerProvider(ILogBuffer buffer, LogFileSink fileSink, LogLevelOverrideProvider levels)
    {
        _buffer = buffer;
        _fileSink = fileSink;
        _levels = levels;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new VoraLogger(name, _buffer, _fileSink, _levels));

    public void Dispose()
    {
        _loggers.Clear();
    }

    private sealed class VoraLogger : ILogger
    {
        private readonly string _category;
        private readonly ILogBuffer _buffer;
        private readonly LogFileSink _fileSink;
        private readonly LogLevelOverrideProvider _levels;

        public VoraLogger(string category, ILogBuffer buffer, LogFileSink fileSink, LogLevelOverrideProvider levels)
        {
            _category = category;
            _buffer = buffer;
            _fileSink = fileSink;
            _levels = levels;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None) return false;
            var v = Map(logLevel);
            return _levels.IsEnabled(_category, v);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception) ?? string.Empty;
            var entry = new LogEntry(
                Id: _buffer.NextId(),
                TimestampUtc: DateTime.UtcNow,
                Level: Map(logLevel),
                Category: _category,
                EventId: eventId.Id,
                Message: message,
                Exception: exception?.ToString(),
                Scope: null);

            _buffer.Append(entry);
            _fileSink.Enqueue(entry);
        }

        private static VoraLogLevel Map(LogLevel level) => level switch
        {
            LogLevel.Trace => VoraLogLevel.Trace,
            LogLevel.Debug => VoraLogLevel.Debug,
            LogLevel.Information => VoraLogLevel.Information,
            LogLevel.Warning => VoraLogLevel.Warning,
            LogLevel.Error => VoraLogLevel.Error,
            LogLevel.Critical => VoraLogLevel.Critical,
            _ => VoraLogLevel.Information
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
