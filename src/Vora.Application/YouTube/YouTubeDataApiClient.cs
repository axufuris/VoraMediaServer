using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Vora.Application.YouTube.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.YouTube;

namespace Vora.Application.YouTube;

public sealed class YouTubeDataApiClient : IYouTubeDataApiClient
{
    public const string DataApiHttpClientName = "YouTubeDataApi";
    public const string RssHttpClientName = "YouTubeRss";

    private const string DataApiBaseUrl = "https://www.googleapis.com/youtube/v3/";
    private const string RssBaseUrl = "https://www.youtube.com/feeds/videos.xml";

    private static readonly TimeSpan TrendingTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ChannelMetadataTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ChannelUploadsTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RelatedTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan VideosByIdTtl = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginSettingsProvider _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<YouTubeDataApiClient> _logger;

    public YouTubeDataApiClient(
        IHttpClientFactory httpClientFactory,
        IPluginSettingsProvider settings,
        IMemoryCache cache,
        ILogger<YouTubeDataApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var key = await _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.ApiKeySettingKey);
        return !string.IsNullOrWhiteSpace(key);
    }

    public async Task<List<YouTubeVideoDto>> GetTrendingAsync(string regionCode, YouTubeSafeSearchLevel safeSearch, CancellationToken ct = default)
    {
        var region = string.IsNullOrWhiteSpace(regionCode) ? "US" : regionCode.Trim().ToUpperInvariant();
        var cacheKey = $"yt:trending:{region}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TrendingTtl;

            var apiKey = await GetApiKeyAsync();
            if (apiKey is null) return new List<YouTubeVideoDto>();

            var url = $"videos?part=snippet,contentDetails,statistics&chart=mostPopular&maxResults=25&regionCode={Uri.EscapeDataString(region)}&key={apiKey}";
            return await FetchVideoListAsync(url, ct);
        });

        var list = cached ?? new List<YouTubeVideoDto>();
        return ApplyContentFilters(list, safeSearch);
    }

    public async Task<List<YouTubeVideoDto>> SearchAsync(string query, YouTubeSafeSearchLevel safeSearch, int maxResults = 20, CancellationToken ct = default)
    {
        var page = await SearchPageAsync(query, safeSearch, maxResults, null, ct);
        return page.Videos;
    }

    public async Task<YouTubeSearchPageDto> SearchPageAsync(string query, YouTubeSafeSearchLevel safeSearch, int maxResults = 20, string? pageToken = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new YouTubeSearchPageDto();

        var normalized = query.Trim();
        var safe = SafeSearchToParam(safeSearch);
        var tokenKey = string.IsNullOrEmpty(pageToken) ? "page1" : pageToken;
        var cacheKey = $"yt:search:{safe}:{normalized.ToLowerInvariant()}:{maxResults}:{tokenKey}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SearchTtl;

            var apiKey = await GetApiKeyAsync();
            if (apiKey is null) return new YouTubeSearchPageDto();

            var url = $"search?part=snippet&type=video&maxResults={maxResults}&safeSearch={safe}&q={Uri.EscapeDataString(normalized)}&key={apiKey}";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var (ids, nextToken) = await FetchSearchPageAsync(url, ct);
            if (ids.Count == 0)
            {
                return new YouTubeSearchPageDto { NextPageToken = nextToken };
            }

            var videos = await GetVideosByIdInternalAsync(ids, ct);
            return new YouTubeSearchPageDto { Videos = videos, NextPageToken = nextToken };
        });

        var page = cached ?? new YouTubeSearchPageDto();
        return new YouTubeSearchPageDto
        {
            Videos = ApplyContentFilters(page.Videos, safeSearch),
            NextPageToken = page.NextPageToken
        };
    }

    public async Task<List<YouTubeVideoDto>> GetVideosByIdAsync(IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var ids = videoIds.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        if (ids.Count == 0) return new List<YouTubeVideoDto>();

        return await GetVideosByIdInternalAsync(ids, ct);
    }

    public async Task<YouTubeChannelDto?> GetChannelAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return null;
        var cacheKey = $"yt:channel:{channelId}";

        if (_cache.TryGetValue(cacheKey, out YouTubeChannelDto? cached) && cached is not null)
        {
            return cached;
        }

        var apiKey = await GetApiKeyAsync();
        if (apiKey is null) return null;

        var url = $"channels?part=snippet,statistics,contentDetails&id={Uri.EscapeDataString(channelId)}&key={apiKey}";

        using var client = CreateDataApiClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("YouTube channel lookup failed for {ChannelId}: {Status} {Body}", channelId, response.StatusCode, Truncate(body, 256));
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            _logger.LogInformation("YouTube channel lookup returned no items for {ChannelId}", channelId);
            return null;
        }

        var item = items[0];
        var snippet = item.TryGetProperty("snippet", out var s) ? s : default;
        var statistics = item.TryGetProperty("statistics", out var st) ? st : default;
        var content = item.TryGetProperty("contentDetails", out var c) ? c : default;

        var dto = new YouTubeChannelDto
        {
            ChannelId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? channelId : channelId,
            Title = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
            Description = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("description", out var d) ? d.GetString() : null,
            ThumbnailUrl = snippet.ValueKind == JsonValueKind.Object ? ExtractThumbnailUrl(snippet) : null,
            SubscriberCount = statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("subscriberCount", out var sub) && long.TryParse(sub.GetString(), out var subVal) ? subVal : null,
            VideoCount = statistics.ValueKind == JsonValueKind.Object && statistics.TryGetProperty("videoCount", out var vc) && long.TryParse(vc.GetString(), out var vcVal) ? vcVal : null,
            UploadsPlaylistId = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("relatedPlaylists", out var rp) && rp.TryGetProperty("uploads", out var up) ? up.GetString() : null
        };

        _cache.Set(cacheKey, dto, ChannelMetadataTtl);
        return dto;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max));

    public async Task<List<YouTubePlaylistDto>> GetChannelPlaylistsAsync(string channelId, int maxResults = 25, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return new List<YouTubePlaylistDto>();

        var cacheKey = $"yt:channel-playlists:{channelId}:{maxResults}";
        if (_cache.TryGetValue(cacheKey, out List<YouTubePlaylistDto>? cached) && cached is not null)
        {
            return cached;
        }

        var apiKey = await GetApiKeyAsync();
        if (apiKey is null) return new List<YouTubePlaylistDto>();

        var url = $"playlists?part=snippet,contentDetails&channelId={Uri.EscapeDataString(channelId)}&maxResults={maxResults}&key={apiKey}";

        using var client = CreateDataApiClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("YouTube channel playlists lookup failed for {ChannelId}: {Status} {Body}", channelId, response.StatusCode, Truncate(body, 256));
            return new List<YouTubePlaylistDto>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var playlists = new List<YouTubePlaylistDto>();
        if (!doc.RootElement.TryGetProperty("items", out var items)) return playlists;

        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var snippet = item.TryGetProperty("snippet", out var s) ? s : default;
            var contentDetails = item.TryGetProperty("contentDetails", out var c) ? c : default;

            playlists.Add(new YouTubePlaylistDto
            {
                PlaylistId = id,
                Title = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                Description = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("description", out var d) ? d.GetString() : null,
                ThumbnailUrl = snippet.ValueKind == JsonValueKind.Object ? ExtractThumbnailUrl(snippet) : null,
                ItemCount = contentDetails.ValueKind == JsonValueKind.Object && contentDetails.TryGetProperty("itemCount", out var ic) && ic.TryGetInt32(out var icVal) ? icVal : null,
                PublishedAt = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("publishedAt", out var pa) && DateTimeOffset.TryParse(pa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts) ? ts : null,
            });
        }

        _cache.Set(cacheKey, playlists, ChannelMetadataTtl);
        return playlists;
    }

    public async Task<List<YouTubeVideoDto>> GetChannelRecentUploadsAsync(string channelId, int maxResults = 15, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return new List<YouTubeVideoDto>();

        var cacheKey = $"yt:uploads:{channelId}:{maxResults}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ChannelUploadsTtl;
            return await FetchRecentUploadsViaRssAsync(channelId, maxResults, ct);
        });

        return cached ?? new List<YouTubeVideoDto>();
    }

    public async Task<YouTubeSearchPageDto> GetChannelUploadsPageAsync(string channelId, string? pageToken = null, int maxResults = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId)) return new YouTubeSearchPageDto();

        var tokenKey = string.IsNullOrEmpty(pageToken) ? "page1" : pageToken;
        var cacheKey = $"yt:uploads-paged:{channelId}:{maxResults}:{tokenKey}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ChannelUploadsTtl;

            var apiKey = await GetApiKeyAsync();
            if (apiKey is null) return new YouTubeSearchPageDto();

            var channel = await GetChannelAsync(channelId, ct);
            var uploadsPlaylistId = channel?.UploadsPlaylistId;
            if (string.IsNullOrWhiteSpace(uploadsPlaylistId)) return new YouTubeSearchPageDto();

            var url = $"playlistItems?part=snippet,contentDetails&playlistId={Uri.EscapeDataString(uploadsPlaylistId)}&maxResults={maxResults}&key={apiKey}";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var (ids, nextToken) = await FetchPlaylistVideoIdsAsync(url, ct);
            if (ids.Count == 0) return new YouTubeSearchPageDto { NextPageToken = nextToken };

            var videos = await GetVideosByIdInternalAsync(ids, ct);
            return new YouTubeSearchPageDto { Videos = videos, NextPageToken = nextToken };
        });

        return cached ?? new YouTubeSearchPageDto();
    }

    private async Task<(List<string> Ids, string? NextPageToken)> FetchPlaylistVideoIdsAsync(string url, CancellationToken ct)
    {
        using var client = CreateDataApiClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube playlistItems request failed: {Status}", response.StatusCode);
            return (new List<string>(), null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var ids = new List<string>();
        string? nextPageToken = null;
        if (doc.RootElement.TryGetProperty("nextPageToken", out var tokenEl))
        {
            nextPageToken = tokenEl.GetString();
        }

        if (!doc.RootElement.TryGetProperty("items", out var items)) return (ids, nextPageToken);

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("contentDetails", out var content) && content.TryGetProperty("videoId", out var vid))
            {
                var value = vid.GetString();
                if (!string.IsNullOrEmpty(value)) ids.Add(value);
            }
        }
        return (ids, nextPageToken);
    }

    public async Task<List<YouTubeVideoDto>> GetRelatedVideosAsync(string videoId, YouTubeSafeSearchLevel safeSearch, int maxResults = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return new List<YouTubeVideoDto>();

        var safe = SafeSearchToParam(safeSearch);
        var cacheKey = $"yt:related:{safe}:{videoId}:{maxResults}";

        var cached = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = RelatedTtl;

            var apiKey = await GetApiKeyAsync();
            if (apiKey is null) return new List<YouTubeVideoDto>();

            var sourceVideos = await GetVideosByIdInternalAsync(new List<string> { videoId }, ct);
            var source = sourceVideos.FirstOrDefault();
            if (source is null) return new List<YouTubeVideoDto>();

            var queryTerms = !string.IsNullOrWhiteSpace(source.Title)
                ? source.Title
                : source.ChannelName;
            if (string.IsNullOrWhiteSpace(queryTerms)) return new List<YouTubeVideoDto>();

            var url = $"search?part=snippet&type=video&maxResults={maxResults}&safeSearch={safe}&q={Uri.EscapeDataString(queryTerms)}&key={apiKey}";
            var ids = await FetchSearchIdsAsync(url, ct);
            ids.RemoveAll(id => string.Equals(id, videoId, StringComparison.Ordinal));
            if (ids.Count == 0) return new List<YouTubeVideoDto>();

            return await GetVideosByIdInternalAsync(ids, ct);
        });

        var list = cached ?? new List<YouTubeVideoDto>();
        return ApplyContentFilters(list, safeSearch);
    }

    private async Task<List<YouTubeVideoDto>> GetVideosByIdInternalAsync(List<string> ids, CancellationToken ct)
    {
        var ordered = ids.Distinct().ToList();
        var result = new List<YouTubeVideoDto>(ordered.Count);

        foreach (var chunk in Chunk(ordered, 50))
        {
            var key = $"yt:videosByIdV2:{string.Join(',', chunk.Order())}";
            var chunkResult = await _cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = VideosByIdTtl;

                var apiKey = await GetApiKeyAsync();
                if (apiKey is null) return new List<YouTubeVideoDto>();

                var idList = string.Join(',', chunk);
                var url = $"videos?part=snippet,contentDetails,statistics,player&maxWidth=960&maxHeight=720&id={Uri.EscapeDataString(idList)}&key={apiKey}";
                return await FetchVideoListAsync(url, ct);
            });

            if (chunkResult is null) continue;

            foreach (var id in chunk)
            {
                var match = chunkResult.FirstOrDefault(v => string.Equals(v.VideoId, id, StringComparison.Ordinal));
                if (match is not null) result.Add(match);
            }
        }

        return result;
    }

    private async Task<List<YouTubeVideoDto>> FetchVideoListAsync(string url, CancellationToken ct)
    {
        using var client = CreateDataApiClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube videos request failed: {Status}", response.StatusCode);
            return new List<YouTubeVideoDto>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var videos = new List<YouTubeVideoDto>();
        if (!doc.RootElement.TryGetProperty("items", out var items)) return videos;

        foreach (var item in items.EnumerateArray())
        {
            videos.Add(ParseVideo(item));
        }

        return videos;
    }

    private async Task<List<string>> FetchSearchIdsAsync(string url, CancellationToken ct)
    {
        var (ids, _) = await FetchSearchPageAsync(url, ct);
        return ids;
    }

    private async Task<(List<string> Ids, string? NextPageToken)> FetchSearchPageAsync(string url, CancellationToken ct)
    {
        using var client = CreateDataApiClient();
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("YouTube search request failed: {Status}", response.StatusCode);
            return (new List<string>(), null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var ids = new List<string>();
        string? nextPageToken = null;
        if (doc.RootElement.TryGetProperty("nextPageToken", out var tokenEl))
        {
            nextPageToken = tokenEl.GetString();
        }

        if (!doc.RootElement.TryGetProperty("items", out var items)) return (ids, nextPageToken);

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.TryGetProperty("videoId", out var vid))
            {
                var value = vid.GetString();
                if (!string.IsNullOrEmpty(value)) ids.Add(value);
            }
        }
        return (ids, nextPageToken);
    }

    private async Task<List<YouTubeVideoDto>> FetchRecentUploadsViaRssAsync(string channelId, int maxResults, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(RssHttpClientName);
        var url = $"{RssBaseUrl}?channel_id={Uri.EscapeDataString(channelId)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("YouTube channel RSS not available for {ChannelId} ({Status})", channelId, response.StatusCode);
                return new List<YouTubeVideoDto>();
            }

            var xml = await response.Content.ReadAsStringAsync(ct);
            var doc = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            XNamespace yt = "http://www.youtube.com/xml/schemas/2015";
            XNamespace media = "http://search.yahoo.com/mrss/";

            var channelNameElement = doc.Root?.Element(atom + "author")?.Element(atom + "name");
            var channelName = channelNameElement?.Value ?? string.Empty;

            var videos = new List<YouTubeVideoDto>();
            foreach (var entry in doc.Descendants(atom + "entry").Take(maxResults))
            {
                var videoId = entry.Element(yt + "videoId")?.Value;
                if (string.IsNullOrWhiteSpace(videoId)) continue;

                var title = entry.Element(atom + "title")?.Value ?? string.Empty;
                var published = entry.Element(atom + "published")?.Value;
                var mediaGroup = entry.Element(media + "group");
                var description = mediaGroup?.Element(media + "description")?.Value;
                var thumbnail = mediaGroup?.Element(media + "thumbnail")?.Attribute("url")?.Value;

                videos.Add(new YouTubeVideoDto
                {
                    VideoId = videoId,
                    Title = title,
                    Description = description,
                    ThumbnailUrl = thumbnail ?? $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
                    ChannelId = channelId,
                    ChannelName = channelName,
                    PublishedAt = DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts) ? ts : null
                });
            }
            return videos;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read YouTube RSS for channel {ChannelId}", channelId);
            return new List<YouTubeVideoDto>();
        }
    }

    private static YouTubeVideoDto ParseVideo(JsonElement item)
    {
        var snippet = item.TryGetProperty("snippet", out var s) ? s : default;
        var stats = item.TryGetProperty("statistics", out var st) ? st : default;
        var content = item.TryGetProperty("contentDetails", out var c) ? c : default;

        var dto = new YouTubeVideoDto
        {
            VideoId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty,
            Title = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
            Description = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("description", out var d) ? d.GetString() : null,
            ChannelId = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("channelId", out var cid) ? cid.GetString() ?? string.Empty : string.Empty,
            ChannelName = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("channelTitle", out var cn) ? cn.GetString() ?? string.Empty : string.Empty,
            ThumbnailUrl = snippet.ValueKind == JsonValueKind.Object ? ExtractThumbnailUrl(snippet) ?? string.Empty : string.Empty,
            PublishedAt = snippet.ValueKind == JsonValueKind.Object && snippet.TryGetProperty("publishedAt", out var pa) && DateTimeOffset.TryParse(pa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts) ? ts : null,
            ViewCount = stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty("viewCount", out var vc) && long.TryParse(vc.GetString(), out var vcVal) ? vcVal : null,
            DurationSeconds = content.ValueKind == JsonValueKind.Object && content.TryGetProperty("duration", out var dur) ? ParseIso8601Duration(dur.GetString()) : null
        };

        if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("contentRating", out var rating) && rating.ValueKind == JsonValueKind.Object)
        {
            if (rating.TryGetProperty("mpaaRating", out var mp)) dto.MpaaRating = mp.GetString();
            if (rating.TryGetProperty("tvpgRating", out var tv)) dto.TvpgRating = tv.GetString();
            if (rating.TryGetProperty("ytRating", out var yt)) dto.YtRating = yt.GetString();
        }

        if (item.TryGetProperty("player", out var player) && player.ValueKind == JsonValueKind.Object)
        {
            if (player.TryGetProperty("embedWidth", out var ew))
            {
                if (ew.ValueKind == JsonValueKind.Number && ew.TryGetInt32(out var ewInt)) dto.EmbedWidth = ewInt;
                else if (ew.ValueKind == JsonValueKind.String && int.TryParse(ew.GetString(), out var ewStr)) dto.EmbedWidth = ewStr;
            }
            if (player.TryGetProperty("embedHeight", out var eh))
            {
                if (eh.ValueKind == JsonValueKind.Number && eh.TryGetInt32(out var ehInt)) dto.EmbedHeight = ehInt;
                else if (eh.ValueKind == JsonValueKind.String && int.TryParse(eh.GetString(), out var ehStr)) dto.EmbedHeight = ehStr;
            }
        }

        return dto;
    }

    private static string? ExtractThumbnailUrl(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out var thumbs) || thumbs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in new[] { "maxres", "high", "medium", "default", "standard" })
        {
            if (thumbs.TryGetProperty(key, out var entry) && entry.TryGetProperty("url", out var url))
            {
                var value = url.GetString();
                if (!string.IsNullOrEmpty(value)) return value;
            }
        }
        return null;
    }

    private static int? ParseIso8601Duration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return null;

        var match = Regex.Match(duration, @"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$");
        if (!match.Success) return null;

        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;

        return (hours * 3600) + (minutes * 60) + seconds;
    }

    private static IEnumerable<List<string>> Chunk(List<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }

    private static string SafeSearchToParam(YouTubeSafeSearchLevel level) => level switch
    {
        YouTubeSafeSearchLevel.Strict => "strict",
        YouTubeSafeSearchLevel.None => "none",
        _ => "moderate"
    };

    private static List<YouTubeVideoDto> ApplyContentFilters(List<YouTubeVideoDto> videos, YouTubeSafeSearchLevel level)
    {
        if (level != YouTubeSafeSearchLevel.Strict) return videos;

        return videos
            .Where(v => !string.Equals(v.YtRating, "ytAgeRestricted", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<string?> GetApiKeyAsync()
    {
        var key = await _settings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.ApiKeySettingKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogDebug("YouTube API key is not configured.");
            return null;
        }
        return key;
    }

    private HttpClient CreateDataApiClient()
    {
        var client = _httpClientFactory.CreateClient(DataApiHttpClientName);
        if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri(DataApiBaseUrl);
        }
        return client;
    }
}
