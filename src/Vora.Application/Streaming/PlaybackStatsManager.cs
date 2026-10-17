using System.Collections.Concurrent;
using Vora.Application.Analysis;
using Vora.Application.Analysis.Results;
using Vora.Application.Streaming.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming;

public interface IPlaybackStatsManager
{
    Task<PlaybackStatsVM?> GetAsync(Guid sessionId, Guid accountId, CancellationToken cancellationToken = default);
}

public sealed class PlaybackStatsManager(
    IStreamRepository repository,
    ITranscodeService transcoder,
    IMediaAnalyzerService analyzer,
    StreamFactsCache cache) : IPlaybackStatsManager
{
    public async Task<PlaybackStatsVM?> GetAsync(Guid sessionId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var session = await repository.GetSessionAsync(sessionId);
        if (session == null || session.UserId != accountId) return null;

        var part = await repository.GetMediaPartForSessionAsync(sessionId);
        var facts = part == null
            ? []
            : await cache.GetOrReadAsync(part.FilePath, path => analyzer.ReadStreamFactsAsync(path, cancellationToken));
        var transcode = session.Strategy == nameof(StreamStrategy.DirectPlay)
            ? null
            : transcoder.GetStatus(session.ExtraId ?? session.MediaItemId);

        return PlaybackStats.Build(session, part, facts, transcode);
    }
}

public sealed class StreamFactsCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const int Capacity = 64;

    private readonly ConcurrentDictionary<string, (DateTime ReadAt, IReadOnlyList<MediaStreamFacts> Facts)> _entries = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<MediaStreamFacts>> GetOrReadAsync(string filePath, Func<string, Task<IReadOnlyList<MediaStreamFacts>>> read)
    {
        if (_entries.TryGetValue(filePath, out var entry) && DateTime.UtcNow - entry.ReadAt < Lifetime) return entry.Facts;

        var facts = await read(filePath);
        if (facts.Count == 0) return facts;

        foreach (var stale in _entries.OrderBy(e => e.Value.ReadAt).Take(Math.Max(0, _entries.Count - Capacity + 1)).Select(e => e.Key).ToList())
        {
            _entries.TryRemove(stale, out _);
        }
        _entries[filePath] = (DateTime.UtcNow, facts);
        return facts;
    }
}
