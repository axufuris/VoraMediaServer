using System.Text;
using System.Text.Json;
using Vora.Application.Logging.ViewModels;

namespace Vora.Application.Logging;

public interface ILogManager
{
    LogQueryResultVM Query(LogQueryRequest request);
    Stream Export(LogQueryRequest request, string format, out string contentType, out string fileName);
    LogLevelStateVM GetLevelState();
    void SetLevel(string category, VoraLogLevel level);
    bool ClearOverride(string category);
    IReadOnlyList<string> GetKnownCategories();
}

public sealed class LogManager : ILogManager
{
    private readonly ILogBuffer _buffer;
    private readonly LogLevelOverrideProvider _levels;

    public LogManager(ILogBuffer buffer, LogLevelOverrideProvider levels)
    {
        _buffer = buffer;
        _levels = levels;
    }

    public LogQueryResultVM Query(LogQueryRequest request)
    {
        var all = _buffer.Snapshot();
        var filtered = ApplyFilter(all, request).ToList();

        var limit = request.Limit <= 0 ? 500 : Math.Min(request.Limit, 2000);
        var page = filtered.TakeLast(limit).ToList();

        return new LogQueryResultVM
        {
            Entries = page.Select(ToVM).ToList(),
            TotalMatched = filtered.Count,
            MoreAvailable = filtered.Count > page.Count,
            OldestId = page.Count > 0 ? page[0].Id : null,
            NewestId = page.Count > 0 ? page[^1].Id : null
        };
    }

    public Stream Export(LogQueryRequest request, string format, out string contentType, out string fileName)
    {
        var all = _buffer.Snapshot();
        var filtered = ApplyFilter(all, request).ToList();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "application/json";
            fileName = $"vora-logs-{stamp}.json";
            var json = JsonSerializer.SerializeToUtf8Bytes(filtered.Select(ToVM).ToList());
            return new MemoryStream(json);
        }

        contentType = "text/plain";
        fileName = $"vora-logs-{stamp}.txt";

        var sb = new StringBuilder();
        foreach (var e in filtered)
        {
            sb.Append(e.TimestampUtc.ToString("O")).Append(' ')
              .Append('[').Append(e.Level.ToString().ToUpperInvariant()).Append(']').Append(' ')
              .Append(e.Category).Append(": ").Append(e.Message).AppendLine();
            if (!string.IsNullOrEmpty(e.Exception))
            {
                sb.AppendLine(e.Exception);
            }
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    public LogLevelStateVM GetLevelState()
    {
        var known = GetKnownCategories();
        var overrides = _levels.Overrides
            .Select(kvp => new LogLevelEntryVM
            {
                Category = kvp.Key,
                Level = kvp.Value,
                IsOverride = true
            })
            .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LogLevelStateVM
        {
            DefaultLevel = _levels.DefaultLevel,
            Overrides = overrides,
            KnownCategories = known.ToList()
        };
    }

    public void SetLevel(string category, VoraLogLevel level) => _levels.SetOverride(category, level);

    public bool ClearOverride(string category) => _levels.RemoveOverride(category);

    public IReadOnlyList<string> GetKnownCategories()
    {
        return _buffer.Snapshot()
            .Select(e => e.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<LogEntry> ApplyFilter(IReadOnlyList<LogEntry> all, LogQueryRequest request)
    {
        IEnumerable<LogEntry> q = all;

        if (request.Levels is { Count: > 0 })
        {
            var set = request.Levels.ToHashSet();
            q = q.Where(e => set.Contains(e.Level));
        }

        if (!string.IsNullOrWhiteSpace(request.CategoryPrefix))
        {
            var prefix = request.CategoryPrefix.Trim();
            q = q.Where(e => e.Category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var needle = request.Search.Trim();
            q = q.Where(e =>
                e.Message.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (e.Exception != null && e.Exception.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        if (request.SinceUtc.HasValue)
        {
            var since = request.SinceUtc.Value;
            q = q.Where(e => e.TimestampUtc >= since);
        }

        if (request.UntilUtc.HasValue)
        {
            var until = request.UntilUtc.Value;
            q = q.Where(e => e.TimestampUtc <= until);
        }

        if (request.BeforeId.HasValue)
        {
            var before = request.BeforeId.Value;
            q = q.Where(e => e.Id < before);
        }

        return q;
    }

    private static LogEntryVM ToVM(LogEntry e) => new()
    {
        Id = e.Id,
        TimestampUtc = e.TimestampUtc,
        Level = e.Level,
        Category = e.Category,
        EventId = e.EventId,
        Message = e.Message,
        Exception = e.Exception
    };
}
