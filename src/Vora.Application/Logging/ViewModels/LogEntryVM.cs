namespace Vora.Application.Logging.ViewModels;

public sealed class LogEntryVM
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public required VoraLogLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public bool HasException => !string.IsNullOrEmpty(Exception);
}

public sealed class LogQueryRequest
{
    public List<VoraLogLevel>? Levels { get; set; }
    public string? CategoryPrefix { get; set; }
    public string? Search { get; set; }
    public DateTime? SinceUtc { get; set; }
    public DateTime? UntilUtc { get; set; }
    public long? BeforeId { get; set; }
    public int Limit { get; set; } = 500;
}

public sealed class LogQueryResultVM
{
    public List<LogEntryVM> Entries { get; set; } = new();
    public int TotalMatched { get; set; }
    public bool MoreAvailable { get; set; }
    public long? OldestId { get; set; }
    public long? NewestId { get; set; }
}

public sealed class LogLevelEntryVM
{
    public string Category { get; set; } = string.Empty;
    public VoraLogLevel Level { get; set; }
    public bool IsOverride { get; set; }
}

public sealed class LogLevelStateVM
{
    public VoraLogLevel DefaultLevel { get; set; }
    public List<LogLevelEntryVM> Overrides { get; set; } = new();
    public List<string> KnownCategories { get; set; } = new();
}

public sealed class SetLevelRequest
{
    public required VoraLogLevel Level { get; set; }
}
