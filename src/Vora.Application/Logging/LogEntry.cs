namespace Vora.Application.Logging;

public enum VoraLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

public sealed record LogEntry(
    long Id,
    DateTime TimestampUtc,
    VoraLogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    string? Scope);
