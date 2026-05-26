using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tmdb;

public class TmdbDiscoveryProvider : IDiscoveryProvider
{
    private const long CacheEntrySize = 1024;

    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public string Id => "tmdb_discovery";
    public string Name => "TMDB Discovery Engine";
    public string ProviderName => "TMDB";
    public string Version => "1.0.0";
    public string Description => "Provides dynamic lists of popular, trending, and upcoming media from TMDB.";
    public bool IsSystemPlugin => true;
    public string Type => "Discovery";

    public TmdbDiscoveryProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "discovery_region",
                Label = "Discovery Region",
                Type = "text",
                Description = "ISO 3166-1 country code (e.g., US, GB, CA) to filter the discovery lists. Defaults to US if left blank."
            },
            new PluginSettingDefinitionDto
            {
                Key = "discovery_language",
                Label = "Discovery Language",
                Type = "text",
                Description = "ISO 639-1 language code (e.g., en-US). Helps filter out foreign titles. Defaults to en-US if left blank."
            }
        };
    }

    private async Task<string?> GetApiKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync("tmdb_metadata", "api_key");
    }

    private async Task<string> GetRegionAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var region = await settings.GetSettingAsync(Id, "discovery_region");
        return string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpper();
    }

    private async Task<string> GetLanguageAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var language = await settings.GetSettingAsync(Id, "discovery_language");
        return string.IsNullOrWhiteSpace(language) ? "en-US" : language.Trim();
    }

    public async Task<IEnumerable<DiscoveryRowDefinitionDto>> GetAvailableRowsAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrEmpty(apiKey)) return new List<DiscoveryRowDefinitionDto>();

        return new List<DiscoveryRowDefinitionDto>
        {
            new() { Id = "movie_popular", Name = "Popular Movies", ProviderId = Id },
            new() { Id = "movie_upcoming", Name = "Upcoming Movies", ProviderId = Id },
            new() { Id = "movie_top_rated", Name = "Top Rated Movies", ProviderId = Id },
            new() { Id = "tv_popular", Name = "Popular TV Shows", ProviderId = Id },
            new() { Id = "tv_airing_today", Name = "Airing Today", ProviderId = Id }
        };
    }

    public async Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string rowId, int page = 1, CancellationToken cancellationToken = default)
    {
        var region = await GetRegionAsync();
        var language = await GetLanguageAsync();

        var cacheKey = $"tmdb_row_{rowId}_{page}_{region}_{language}";

        var cachedItems = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            entry.Size = CacheEntrySize;

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey)) return new List<DiscoveryItemDto>();

            string url = "";
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string nextMonth = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

            switch (rowId)
            {
                case "movie_popular":
                    url = $"discover/movie?sort_by=popularity.desc&with_origin_country={region}&language={language}&page={page}";
                    break;
                case "movie_upcoming":
                    url = $"discover/movie?primary_release_date.gte={today}&primary_release_date.lte={nextMonth}&with_origin_country={region}&language={language}&page={page}";
                    break;
                case "movie_top_rated":
                    url = $"discover/movie?sort_by=vote_average.desc&vote_count.gte=500&with_origin_country={region}&language={language}&page={page}";
                    break;
                case "tv_popular":
                    url = $"discover/tv?sort_by=popularity.desc&with_origin_country={region}&language={language}&page={page}";
                    break;
                case "tv_airing_today":
                    url = $"discover/tv?air_date.gte={today}&air_date.lte={today}&with_origin_country={region}&language={language}&page={page}";
                    break;
            }

            if (string.IsNullOrEmpty(url)) return new List<DiscoveryItemDto>();

            var response = await _httpClient.GetAsync($"{url}&api_key={apiKey}", cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<DiscoveryItemDto>();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var results = new List<DiscoveryItemDto>();

            bool isTv = rowId.StartsWith("tv_");

            foreach (var el in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var rawDate = el.TryGetProperty(isTv ? "first_air_date" : "release_date", out var d) ? d.GetString() : "";
                var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

                results.Add(new DiscoveryItemDto
                {
                    ExternalId = el.GetProperty("id").GetInt32().ToString(),
                    ProviderId = Id,
                    Type = isTv ? "TvShow" : "Movie",
                    Title = el.TryGetProperty(isTv ? "name" : "title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                    PosterUrl = el.TryGetProperty("poster_path", out var p) && p.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w500{p.GetString()}" : null,
                    Year = parsedDate?.Year,
                    ReleaseDate = parsedDate
                });
            }

            return results;
        });

        return cachedItems ?? new List<DiscoveryItemDto>();
    }

    public async Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string externalId, string type, CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync();
        var cacheKey = $"tmdb_details_{type}_{externalId}_{language}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            entry.Size = CacheEntrySize;

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey)) return null;

            var isTv = type.Equals("TvShow", StringComparison.OrdinalIgnoreCase);
            var endpoint = isTv ? $"tv/{externalId}" : $"movie/{externalId}";

            var response = await _httpClient.GetAsync($"{endpoint}?api_key={apiKey}&append_to_response=credits,videos&language={language}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var el = doc.RootElement;

            var rawDate = el.TryGetProperty(isTv ? "first_air_date" : "release_date", out var dStr) ? dStr.GetString() : "";
            var parsedDate = DateTime.TryParse(rawDate, out var dateParsed) ? (DateTime?)DateTime.SpecifyKind(dateParsed, DateTimeKind.Utc) : null;

            DateTime? parsedNextAirDate = null;
            if (isTv && el.TryGetProperty("next_episode_to_air", out var ne) && ne.ValueKind != JsonValueKind.Null)
            {
                var nextRaw = ne.TryGetProperty("air_date", out var nd) ? nd.GetString() : "";
                if (DateTime.TryParse(nextRaw, out var nextParsed))
                {
                    parsedNextAirDate = DateTime.SpecifyKind(nextParsed, DateTimeKind.Utc);
                }
            }

            var details = new DiscoveryItemDetailsDto
            {
                ExternalId = externalId,
                ProviderId = Id,
                Type = type,
                Title = el.TryGetProperty(isTv ? "name" : "title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                Overview = el.TryGetProperty("overview", out var ov) ? ov.GetString() : null,
                PosterUrl = el.TryGetProperty("poster_path", out var p) && p.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w500{p.GetString()}" : null,
                BackgroundUrl = el.TryGetProperty("backdrop_path", out var b) && b.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w1280{b.GetString()}" : null,
                Year = parsedDate?.Year,
                ReleaseDate = parsedDate, // <-- I ACCIDENTALLY DELETED THIS LINE IN THE LAST STEP!
                NextAirDate = parsedNextAirDate
            };

            if (el.TryGetProperty("credits", out var credits) && credits.TryGetProperty("cast", out var cast))
            {
                foreach (var actor in cast.EnumerateArray().Take(15))
                {
                    details.Cast.Add(new CastMemberDto
                    {
                        ExternalId = actor.GetProperty("id").GetInt32().ToString(),
                        Name = actor.GetProperty("name").GetString() ?? "Unknown",
                        Role = actor.TryGetProperty("character", out var c) ? c.GetString() ?? "Actor" : "Actor",
                        ProfileImageUrl = actor.TryGetProperty("profile_path", out var pp) && pp.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w185{pp.GetString()}" : null
                    });
                }
            }

            if (el.TryGetProperty("videos", out var videos) && videos.TryGetProperty("results", out var vids))
            {
                foreach (var vid in vids.EnumerateArray())
                {
                    if (details.Trailers.Count >= 10) break;

                    if (vid.TryGetProperty("site", out var site) && site.GetString() == "YouTube" &&
                        vid.TryGetProperty("type", out var vidType) && vidType.GetString() == "Trailer")
                    {
                        details.Trailers.Add(new TrailerDto
                        {
                            Name = vid.GetProperty("name").GetString() ?? "Trailer",
                            Url = $"https://www.youtube.com/watch?v={vid.GetProperty("key").GetString()}"
                        });
                    }
                }
            }

            return details;
        });
    }

    public async Task<DiscoveryActorDto?> GetActorDetailsAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync();
        var cacheKey = $"tmdb_actor_{externalId}_{language}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            entry.Size = CacheEntrySize;

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey)) return null;

            var response = await _httpClient.GetAsync($"person/{externalId}?api_key={apiKey}&append_to_response=combined_credits&language={language}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var el = doc.RootElement;

            var actor = new DiscoveryActorDto
            {
                ExternalId = externalId,
                ProviderId = Id,
                Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "Unknown" : "Unknown",
                Biography = el.TryGetProperty("biography", out var b) ? b.GetString() : null,
                PlaceOfBirth = el.TryGetProperty("place_of_birth", out var pob) ? pob.GetString() : null,
                Birthday = el.TryGetProperty("birthday", out var bday) ? bday.GetString() : null,
                Deathday = el.TryGetProperty("deathday", out var dday) ? dday.GetString() : null,
                ProfileImageUrl = el.TryGetProperty("profile_path", out var pp) && pp.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w500{pp.GetString()}" : null
            };

            if (el.TryGetProperty("combined_credits", out var credits) && credits.TryGetProperty("cast", out var cast))
            {
                var sortedCast = cast.EnumerateArray()
                    .OrderByDescending(c => c.TryGetProperty("popularity", out var pop) ? pop.GetDouble() : 0)
                    .Take(40);

                foreach (var role in sortedCast)
                {
                    var mediaType = role.TryGetProperty("media_type", out var mt) ? mt.GetString() : "movie";
                    if (mediaType != "movie" && mediaType != "tv") continue;

                    var rawDate = role.TryGetProperty(mediaType == "tv" ? "first_air_date" : "release_date", out var rd) ? rd.GetString() : "";
                    var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

                    actor.Filmography.Add(new DiscoveryItemDto
                    {
                        ExternalId = role.GetProperty("id").GetInt32().ToString(),
                        ProviderId = Id,
                        Type = mediaType == "tv" ? "TvShow" : "Movie",
                        Title = role.TryGetProperty(mediaType == "tv" ? "name" : "title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                        PosterUrl = role.TryGetProperty("poster_path", out var poster) && poster.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w500{poster.GetString()}" : null,
                        Year = parsedDate?.Year,
                        ReleaseDate = parsedDate
                    });
                }
            }

            return actor;
        });
    }

    public async Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var language = await GetLanguageAsync();
        var cacheKey = $"tmdb_search_{query.ToLowerInvariant()}_{language}";

        var cachedSearch = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            entry.Size = CacheEntrySize;

            var apiKey = await GetApiKeyAsync();
            if (string.IsNullOrEmpty(apiKey)) return new List<DiscoveryItemDto>();

            var response = await _httpClient.GetAsync($"search/multi?api_key={apiKey}&query={Uri.EscapeDataString(query)}&language={language}&page=1", cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<DiscoveryItemDto>();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var results = new List<DiscoveryItemDto>();

            foreach (var el in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var mediaType = el.TryGetProperty("media_type", out var mt) ? mt.GetString() : "";
                if (mediaType != "movie" && mediaType != "tv") continue;

                var rawDate = el.TryGetProperty(mediaType == "tv" ? "first_air_date" : "release_date", out var d) ? d.GetString() : "";
                var parsedDate = DateTime.TryParse(rawDate, out var date) ? (DateTime?)DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;

                results.Add(new DiscoveryItemDto
                {
                    ExternalId = el.GetProperty("id").GetInt32().ToString(),
                    ProviderId = Id,
                    Type = mediaType == "tv" ? "TvShow" : "Movie",
                    Title = el.TryGetProperty(mediaType == "tv" ? "name" : "title", out var t) ? t.GetString() ?? "Unknown" : "Unknown",
                    PosterUrl = el.TryGetProperty("poster_path", out var p) && p.ValueKind != JsonValueKind.Null ? $"https://image.tmdb.org/t/p/w500{p.GetString()}" : null,
                    Year = parsedDate?.Year,
                    ReleaseDate = parsedDate
                });
            }

            return results.Take(4);
        });

        return cachedSearch ?? new List<DiscoveryItemDto>();
    }
}
