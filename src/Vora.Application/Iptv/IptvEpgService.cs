using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Vora.Application.Iptv.Dtos;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;

namespace Vora.Application.Iptv;

public interface IIptvEpgService
{
    Task SyncEpgDataAsync(CancellationToken cancellationToken = default);
    Task LoadCacheIntoMemoryAsync();
    Task RemoveChannelsFromCacheAsync(List<string> channelIds);
    Dictionary<string, List<IptvProgramDto>> GetProgramsForChannels(List<string> channelIds, DateTime startTime, DateTime endTime);
    Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime);
    IptvSourceSyncStats? GetSyncStats(Guid sourceId);
    IReadOnlyDictionary<Guid, IptvSourceSyncStats> GetAllSyncStats();
    IReadOnlySet<string> GetCoveredChannelIds();
}

public class IptvSourceSyncStats
{
    public int TotalProgrammes { get; set; }
    public int MatchedProgrammes { get; set; }
    public int MatchedChannels { get; set; }
    public List<string> UnmatchedSamples { get; set; } = new();
    public DateTime SyncedAt { get; set; }
}

public class IptvEpgService : IIptvEpgService
{
    private const string CacheFolder = "Iptv";
    private const string CacheFileName = "epg_cache.json";
    private const string StorageRoot = "Storage";
    private const int EpgPastWindowHours = 4;
    private const int EpgFutureWindowDays = 3;
    private const string GzipExtension = ".gz";
    private const string UnratedRating = "NR";
    private const string RestrictedTitle = "Restricted Content";
    private const string RestrictedDescription = "This program exceeds the content rating limits for this profile.";

    private static readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static ConcurrentDictionary<string, List<IptvProgramDto>> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, IptvSourceSyncStats> _syncStats = new();
    private static HashSet<string> _channelsCovered = new(StringComparer.OrdinalIgnoreCase);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IptvEpgService> _logger;
    private readonly string _cacheFilePath;

    public IptvEpgService(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, ILogger<IptvEpgService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var cacheDirectory = Path.Combine(AppContext.BaseDirectory, StorageRoot, CacheFolder);
        if (!Directory.Exists(cacheDirectory)) Directory.CreateDirectory(cacheDirectory);
        _cacheFilePath = Path.Combine(cacheDirectory, CacheFileName);
    }

    public async Task LoadCacheIntoMemoryAsync()
    {
        if (!File.Exists(_cacheFilePath)) return;

        try
        {
            await using var stream = File.OpenRead(_cacheFilePath);
            var diskCache = await JsonSerializer.DeserializeAsync<Dictionary<string, List<IptvProgramDto>>>(stream);

            if (diskCache != null)
            {
                _memoryCache = new ConcurrentDictionary<string, List<IptvProgramDto>>(diskCache, StringComparer.OrdinalIgnoreCase);
                _logger.LogInformation("Loaded {ChannelCount} channels of EPG data into memory.", _memoryCache.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load EPG cache from disk.");
        }
    }

    public Dictionary<string, List<IptvProgramDto>> GetProgramsForChannels(List<string> channelIds, DateTime startTime, DateTime endTime)
    {
        startTime = startTime.ToUniversalTime();
        endTime = endTime.ToUniversalTime();

        var result = new Dictionary<string, List<IptvProgramDto>>();

        foreach (var channelId in channelIds)
        {
            if (string.IsNullOrWhiteSpace(channelId)) continue;

            if (_memoryCache.TryGetValue(channelId, out var channelPrograms))
            {
                result[channelId] = channelPrograms
                    .Where(p => p.EndTime > startTime && p.StartTime < endTime)
                    .OrderBy(p => p.StartTime)
                    .ToList();
            }
            else
            {
                result[channelId] = new List<IptvProgramDto>();
            }
        }

        return result;
    }

    public async Task SyncEpgDataAsync(CancellationToken cancellationToken = default)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting global IPTV EPG sync across all active sources.");

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIptvRepository>();

            var sources = await repository.GetActiveEpgSourcesAsync(cancellationToken);
            if (sources.Count == 0)
            {
                _logger.LogInformation("No active EPG sources configured — skipping sync.");
                return;
            }

            var allChannels = await repository.GetActiveChannelsAsync(cancellationToken);
            var cutoffTime = DateTime.UtcNow.AddHours(-EpgPastWindowHours);
            var maxFutureTime = DateTime.UtcNow.AddDays(EpgFutureWindowDays);

            var dbIdSamples = allChannels.Take(15).Select(c => c.ExternalChannelId).ToList();
            _logger.LogInformation("EPG sync: {ChannelCount} known channels in DB. Sample IDs: [{Samples}]",
                allChannels.Count,
                string.Join(", ", dbIdSamples));

            var mergedCache = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);
            var claimedBy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources)
            {
                await SyncSourceAsync(repository, source, allChannels, mergedCache, claimedBy, cutoffTime, maxFutureTime, cancellationToken);
            }

            foreach (var kvp in mergedCache)
            {
                kvp.Value.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            }

            await PersistCacheAsync(mergedCache, cancellationToken);
            _memoryCache = new ConcurrentDictionary<string, List<IptvProgramDto>>(mergedCache, StringComparer.OrdinalIgnoreCase);
            _channelsCovered = new HashSet<string>(mergedCache.Keys, StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation("Completed EPG sync. {ChannelCount} channels populated from {SourceCount} sources.", mergedCache.Count, sources.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public IptvSourceSyncStats? GetSyncStats(Guid sourceId) =>
        _syncStats.TryGetValue(sourceId, out var stats) ? stats : null;

    public IReadOnlyDictionary<Guid, IptvSourceSyncStats> GetAllSyncStats() => _syncStats;

    public IReadOnlySet<string> GetCoveredChannelIds() => _channelsCovered;

    public async Task RemoveChannelsFromCacheAsync(List<string> channelIds)
    {
        await _cacheLock.WaitAsync();
        try
        {
            foreach (var id in channelIds)
            {
                _memoryCache.TryRemove(id, out _);
            }

            await using var fileStream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(fileStream, _memoryCache);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public async Task<Dictionary<string, List<IptvProgramDto>>> GetFilteredGuideAsync(Guid userId, Guid profileId, List<string> requestedChannelIds, DateTime startTime, DateTime endTime)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IIptvRepository>();
        var userManager = scope.ServiceProvider.GetRequiredService<IUserManager>();

        var user = await userManager.GetUserAccountAsync(userId);
        var profile = user?.Profiles.FirstOrDefault(p => p.Id == profileId);
        var allChannels = await repository.GetActiveChannelsAsync(CancellationToken.None);

        var validChannels = allChannels.Where(c => !c.IsHiddenByAdmin).ToList();
        if (user != null && !user.IsAdmin && !user.HasAllIptvAccess)
        {
            validChannels = validChannels.Where(c => user.AllowedIptvPlaylistIds.Contains(c.PlaylistId)).ToList();
        }

        if (profile != null && !profile.IsAdmin && !profile.HasAllIptvAccess)
        {
            validChannels = validChannels.Where(c => profile.AllowedIptvPlaylistIds.Contains(c.PlaylistId)).ToList();
        }

        var validChannelIds = validChannels.Select(c => c.ExternalChannelId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetIds = requestedChannelIds.Any()
            ? requestedChannelIds
            : validChannels.Select(c => c.ExternalChannelId).ToList();

        var allowedIds = targetIds.Where(id => validChannelIds.Contains(id)).ToList();

        var rawGuide = GetProgramsForChannels(allowedIds, startTime, endTime);

        if (profile == null || profile.HasAllLibraryAccess) return rawGuide;

        ApplyParentalControls(rawGuide, profile.AllowedTvRatings ?? new List<string>(), profile.BlockUnratedContent);

        return rawGuide;
    }

    private async Task SyncSourceAsync(
        IIptvRepository repository,
        IptvEpgSource source,
        List<IptvChannel> allChannels,
        Dictionary<string, List<IptvProgramDto>> mergedCache,
        Dictionary<string, Guid> claimedBy,
        DateTime cutoffTime,
        DateTime maxFutureTime,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsed = new Dictionary<string, List<IptvProgramDto>>(StringComparer.OrdinalIgnoreCase);

            var httpClient = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
            using var response = await httpClient.GetAsync(source.XmlTvUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            stream = await DecompressIfNeededAsync(stream, source.XmlTvUrl, cancellationToken);

            var stats = await XmlTvParser.ParseAsync(stream, allChannels, parsed, cutoffTime, maxFutureTime, cancellationToken);

            _syncStats[source.Id] = new IptvSourceSyncStats
            {
                TotalProgrammes = stats.ProgrammesSeen,
                MatchedProgrammes = stats.ProgrammesMatched,
                MatchedChannels = parsed.Count,
                UnmatchedSamples = stats.UnmatchedIdSamples.ToList(),
                SyncedAt = DateTime.UtcNow
            };

            _logger.LogInformation(
                "EPG source {Name}: parsed {Seen} programmes, matched {Matched} ({MatchedPct:P0}). Sample unmatched IDs: [{Unmatched}]",
                source.Name,
                stats.ProgrammesSeen,
                stats.ProgrammesMatched,
                stats.ProgrammesSeen == 0 ? 0 : (double)stats.ProgrammesMatched / stats.ProgrammesSeen,
                string.Join(", ", stats.UnmatchedIdSamples));

            ClaimChannelsForSource(source.Id, parsed, mergedCache, claimedBy);

            source.LastError = null;
            source.LastSyncedAt = DateTime.UtcNow;
            await repository.UpdateEpgSourceAsync(source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process XMLTV for EPG source {Name}.", source.Name);
            source.LastError = ex.Message;
            await repository.UpdateEpgSourceAsync(source);
        }
    }

    private static void ClaimChannelsForSource(
        Guid sourceId,
        Dictionary<string, List<IptvProgramDto>> parsed,
        Dictionary<string, List<IptvProgramDto>> mergedCache,
        Dictionary<string, Guid> claimedBy)
    {
        foreach (var (channelId, programs) in parsed)
        {
            if (programs.Count == 0) continue;

            if (claimedBy.TryGetValue(channelId, out var owner) && owner != sourceId)
            {
                continue;
            }

            claimedBy[channelId] = sourceId;

            if (!mergedCache.TryGetValue(channelId, out var existing))
            {
                existing = new List<IptvProgramDto>();
                mergedCache[channelId] = existing;
            }

            var seen = new HashSet<DateTime>(existing.Select(p => p.StartTime));
            foreach (var program in programs)
            {
                if (seen.Add(program.StartTime))
                {
                    existing.Add(program);
                }
            }
        }
    }

    private static async Task<Stream> DecompressIfNeededAsync(Stream stream, string url, CancellationToken cancellationToken)
    {
        if (!url.EndsWith(GzipExtension, StringComparison.OrdinalIgnoreCase))
        {
            return stream;
        }

        var memoryStream = new MemoryStream();
        await using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(memoryStream, cancellationToken);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }

    private async Task PersistCacheAsync(Dictionary<string, List<IptvProgramDto>> cache, CancellationToken cancellationToken)
    {
        await using var fileStream = File.Create(_cacheFilePath);
        await JsonSerializer.SerializeAsync(fileStream, cache, cancellationToken: cancellationToken);
    }

    private static void ApplyParentalControls(Dictionary<string, List<IptvProgramDto>> guide, List<string> allowedRatings, bool blockUnratedContent)
    {
        foreach (var channel in guide)
        {
            foreach (var program in channel.Value)
            {
                if (allowedRatings.Contains(program.ContentRating)) continue;
                if (!blockUnratedContent && program.ContentRating == UnratedRating) continue;

                program.Title = RestrictedTitle;
                program.Description = RestrictedDescription;
            }
        }
    }
}
