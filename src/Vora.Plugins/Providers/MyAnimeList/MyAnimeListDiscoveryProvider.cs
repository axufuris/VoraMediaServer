using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MyAnimeList;

public class MyAnimeListDiscoveryProvider : IDiscoveryProvider
{
    private const long CacheEntrySize = 1024;

    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public string Id => "mal_discovery";
    public string Name => "MyAnimeList Discovery Engine";
    public string ProviderName => "MyAnimeList";
    public string Version => "1.0.0";
    public string Description => "Discover top-rated, trending, and upcoming Anime directly from MyAnimeList.";
    public bool IsSystemPlugin => true;
    public string Type => "Discovery";

    public MyAnimeListDiscoveryProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _httpClient.BaseAddress = new Uri("https://api.myanimelist.net/v2/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    private async Task<string?> GetClientIdAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        return await settings.GetSettingAsync("mal_artwork", "client_id");
    }

    public async Task<IEnumerable<DiscoveryRowDefinitionDto>> GetAvailableRowsAsync()
    {
        var clientId = await GetClientIdAsync();
        if (string.IsNullOrEmpty(clientId)) return new List<DiscoveryRowDefinitionDto>();

        return new List<DiscoveryRowDefinitionDto>
        {
            new() { Id = "mal_airing", Name = "Top Airing Anime", ProviderId = Id },
            new() { Id = "mal_upcoming", Name = "Top Upcoming Anime", ProviderId = Id },
            new() { Id = "mal_popular", Name = "Most Popular Anime", ProviderId = Id },
            new() { Id = "mal_top", Name = "Highest Rated Anime", ProviderId = Id }
        };
    }

    public async Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string rowId, int page = 1)
    {
        var cacheKey = $"mal_row_{rowId}_{page}";

        var cachedItems = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            var clientId = await GetClientIdAsync();
            if (string.IsNullOrEmpty(clientId))
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return new List<DiscoveryItemDto>();
            }

            string rankingType = rowId switch
            {
                "mal_airing" => "airing",
                "mal_upcoming" => "upcoming",
                "mal_popular" => "bypopularity",
                "mal_top" => "all",
                _ => "all"
            };

            int offset = (page - 1) * 20;
            var url = $"anime/ranking?ranking_type={rankingType}&limit=20&offset={offset}&fields=id,title,main_picture,start_date,media_type";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MAL-CLIENT-ID", clientId);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return new List<DiscoveryItemDto>();
            }

            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            entry.Size = CacheEntrySize;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var results = new List<DiscoveryItemDto>();

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var el in dataArray.EnumerateArray())
                {
                    var node = el.GetProperty("node");

                    var mediaType = node.TryGetProperty("media_type", out var mt) ? mt.GetString() : "tv";
                    var isMovie = mediaType == "movie";

                    var rawDate = node.TryGetProperty("start_date", out var d) ? d.GetString() : "";
                    var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

                    results.Add(new DiscoveryItemDto
                    {
                        ExternalId = node.GetProperty("id").GetInt32().ToString(),
                        ProviderId = Id,
                        Type = isMovie ? "Movie" : "TvShow",
                        Title = node.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                        PosterUrl = node.TryGetProperty("main_picture", out var mp) && mp.TryGetProperty("large", out var p) ? p.GetString() : null,
                        Year = parsedDate?.Year,
                        ReleaseDate = parsedDate
                    });
                }
            }

            return results;
        });

        return cachedItems ?? new List<DiscoveryItemDto>();
    }

    public async Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string externalId, string type)
    {
        var cacheKey = $"mal_details_{externalId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            var clientId = await GetClientIdAsync();
            if (string.IsNullOrEmpty(clientId))
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return null;
            }

            var url = $"anime/{externalId}?fields=id,title,main_picture,synopsis,start_date,media_type,studios,pictures";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MAL-CLIENT-ID", clientId);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return null;
            }

            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            entry.Size = CacheEntrySize;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var node = doc.RootElement;

            var mediaType = node.TryGetProperty("media_type", out var mt) ? mt.GetString() : "tv";
            var rawDate = node.TryGetProperty("start_date", out var d) ? d.GetString() : "";
            var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

            var details = new DiscoveryItemDetailsDto
            {
                ExternalId = externalId,
                ProviderId = Id,
                Type = mediaType == "movie" ? "Movie" : "TvShow",
                Title = node.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                Overview = node.TryGetProperty("synopsis", out var syn) ? syn.GetString() : null,
                PosterUrl = node.TryGetProperty("main_picture", out var mp) && mp.TryGetProperty("large", out var p) ? p.GetString() : null,
                Year = parsedDate?.Year,
                ReleaseDate = parsedDate // <-- ADDED THIS!
            };

            if (node.TryGetProperty("pictures", out var pics) && pics.ValueKind == JsonValueKind.Array && pics.GetArrayLength() > 1)
            {
                var altPic = pics[1];
                if (altPic.TryGetProperty("large", out var bp)) details.BackgroundUrl = bp.GetString();
            }

            return details;
        });
    }

    public Task<DiscoveryActorDto?> GetActorDetailsAsync(string externalId)
    {
        return Task.FromResult<DiscoveryActorDto?>(null);
    }

    public async Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query)
    {
        var cacheKey = $"mal_search_{query.ToLowerInvariant()}";

        var cachedSearch = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            var clientId = await GetClientIdAsync();
            if (string.IsNullOrEmpty(clientId))
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return new List<DiscoveryItemDto>();
            }

            var url = $"anime?q={Uri.EscapeDataString(query)}&limit=5&fields=id,title,main_picture,start_date,media_type";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-MAL-CLIENT-ID", clientId);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                entry.Size = CacheEntrySize;
                return new List<DiscoveryItemDto>();
            }

            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            entry.Size = CacheEntrySize;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var results = new List<DiscoveryItemDto>();

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var el in dataArray.EnumerateArray())
                {
                    var node = el.GetProperty("node");
                    var mediaType = node.TryGetProperty("media_type", out var mt) ? mt.GetString() : "tv";
                    var rawDate = node.TryGetProperty("start_date", out var d) ? d.GetString() : "";
                    var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

                    results.Add(new DiscoveryItemDto
                    {
                        ExternalId = node.GetProperty("id").GetInt32().ToString(),
                        ProviderId = Id,
                        Type = mediaType == "movie" ? "Movie" : "TvShow",
                        Title = node.TryGetProperty("title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                        PosterUrl = node.TryGetProperty("main_picture", out var mp) && mp.TryGetProperty("large", out var p) ? p.GetString() : null,
                        Year = parsedDate?.Year,
                        ReleaseDate = parsedDate
                    });
                }
            }

            return results;
        });

        return cachedSearch ?? new List<DiscoveryItemDto>();
    }
}
