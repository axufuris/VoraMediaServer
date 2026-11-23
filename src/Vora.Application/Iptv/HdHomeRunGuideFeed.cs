using System.Collections.Concurrent;
using System.IO.Compression;
using Vora.Application.Iptv.Dtos;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

internal sealed record HdHomeRunGuideResult(Dictionary<string, List<IptvProgramDto>> Programs, IptvSourceSyncStats Stats);

internal sealed class HdHomeRunGuideFeed
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(20);
    private static readonly TimeSpan IntervalSpread = TimeSpan.FromHours(8);
    private static readonly ConcurrentDictionary<Guid, Snapshot> Held = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHdHomeRunClient _device;
    private readonly TimeProvider _time;

    public HdHomeRunGuideFeed(IHttpClientFactory httpClientFactory, IHdHomeRunClient device, TimeProvider time)
    {
        _httpClientFactory = httpClientFactory;
        _device = device;
        _time = time;
    }

    public async Task<HdHomeRunGuideResult?> ReadAsync(
        IptvPlaylist playlist,
        List<IptvChannel> channels,
        DateTime cutoffTime,
        DateTime maxFutureTime,
        CancellationToken cancellationToken)
    {
        if (channels.Count == 0) return null;

        var now = _time.GetUtcNow().UtcDateTime;
        var address = playlist.ServerUrl;
        Held.TryGetValue(playlist.Id, out var held);
        if (held is not null && held.Address != address) held = null;

        if (held is not null && now < held.DueAt)
        {
            return new HdHomeRunGuideResult(Current(held.Programs, cutoffTime), held.Stats);
        }

        string? deviceAuth = null;
        try
        {
            deviceAuth = await _device.GetDeviceAuthAsync(playlist, cancellationToken);
            var feed = await DownloadAsync(deviceAuth, cancellationToken);

            var parsed = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
            var tuners = new HashSet<Guid> { playlist.Id };
            var parseStats = await XmlTvParser.ParseAsync(Decompressed(feed), channels, parsed, cutoffTime, maxFutureTime, cancellationToken, tuners);

            var stats = new IptvSourceSyncStats
            {
                TotalProgrammes = parseStats.ProgrammesSeen,
                MatchedProgrammes = parseStats.ProgrammesMatched,
                MatchedChannels = parsed.Count,
                UnmatchedSamples = parseStats.UnmatchedIdSamples.ToList(),
                SyncedAt = now
            };
            var dueAt = now + MinimumInterval + TimeSpan.FromTicks((long)(Random.Shared.NextDouble() * IntervalSpread.Ticks));
            Held[playlist.Id] = new Snapshot(address, dueAt, parsed, stats);
            return new HdHomeRunGuideResult(parsed, stats);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var failure = new IptvSourceSyncStats { SyncedAt = now, Error = IptvUrlRedactor.Redact(ex.Message, deviceAuth) };
            var kept = held is null ? new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase) : Current(held.Programs, cutoffTime);
            return new HdHomeRunGuideResult(kept, failure);
        }
    }

    private async Task<byte[]> DownloadAsync(string deviceAuth, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, HdHomeRunParser.GuideUrl(deviceAuth));
        request.Headers.AcceptEncoding.ParseAdd("gzip");
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new IptvSourceException($"Silicondust's guide answered HTTP {(int)response.StatusCode}.");
            }
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new IptvSourceException($"Silicondust's guide could not be reached: {ex.Message}", ex);
        }
    }

    private static Stream Decompressed(byte[] feed)
    {
        var raw = new MemoryStream(feed);
        return feed.Length > 2 && feed[0] == 0x1f && feed[1] == 0x8b ? new GZipStream(raw, CompressionMode.Decompress) : raw;
    }

    private static Dictionary<string, List<IptvProgramDto>> Current(Dictionary<string, List<IptvProgramDto>> programs, DateTime cutoffTime) =>
        programs.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Where(p => p.EndTime >= cutoffTime).ToList(),
            StringComparer.OrdinalIgnoreCase);

    private sealed record Snapshot(string? Address, DateTime DueAt, Dictionary<string, List<IptvProgramDto>> Programs, IptvSourceSyncStats Stats);
}
