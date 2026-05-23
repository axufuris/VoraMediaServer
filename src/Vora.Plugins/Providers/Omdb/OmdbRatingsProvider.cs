using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Omdb;

public static class OmdbCircuitBreaker
{
    public static DateTime? BlockedUntil { get; set; }
    public static bool IsBlocked => BlockedUntil.HasValue && DateTime.UtcNow < BlockedUntil.Value;
}

public class OmdbImdbRatingsProvider : IRatingsProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "omdb_imdb";
    public string Name => "OMDb - IMDb Ratings";
    public string Version => "1.0.0";
    public string Description => "Fetches IMDb ratings from the OMDb API.";
    public bool IsSystemPlugin => true;
    public string Type => "Ratings";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public string RatingSourceName => "Internet Movie Database";

    public OmdbImdbRatingsProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("http://www.omdbapi.com/");
        }
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "OMDb API Key",
                Type = "password",
                Description = "OMDb API key. Request a free key at https://www.omdbapi.com/apikey.aspx (1,000 daily requests on the free tier). Click the activation link in the confirmation email before using the key. Paid tiers are available for higher quotas. This single key is shared by the OMDb IMDb, Rotten Tomatoes, and Metacritic ratings providers."
            }
        };
    }

    public async Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType)
    {
        return await OmdbFetcher.FetchRatingCoreAsync(_httpClient, _scopeFactory, imdbId, RatingSourceName);
    }
}

public class OmdbRottenTomatoesRatingsProvider : IRatingsProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "omdb_rotten_tomatoes";
    public string Name => "OMDb - Rotten Tomatoes";
    public string Version => "1.0.0";
    public string Description => "Fetches Rotten Tomatoes ratings from the OMDb API.";
    public bool IsSystemPlugin => true;
    public string Type => "Ratings";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public string RatingSourceName => "Rotten Tomatoes";

    public OmdbRottenTomatoesRatingsProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("http://www.omdbapi.com/");
        }
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType)
    {
        return await OmdbFetcher.FetchRatingCoreAsync(_httpClient, _scopeFactory, imdbId, RatingSourceName);
    }
}

public class OmdbMetacriticRatingsProvider : IRatingsProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "omdb_metacritic";
    public string Name => "OMDb - Metacritic";
    public string Version => "1.0.0";
    public string Description => "Fetches Metacritic ratings from the OMDb API.";
    public bool IsSystemPlugin => true;
    public string Type => "Ratings";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public string RatingSourceName => "Metacritic";

    public OmdbMetacriticRatingsProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("http://www.omdbapi.com/");
        }
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType)
    {
        return await OmdbFetcher.FetchRatingCoreAsync(_httpClient, _scopeFactory, imdbId, RatingSourceName);
    }
}

internal static class OmdbFetcher
{
    public static async Task<decimal?> FetchRatingCoreAsync(HttpClient httpClient, IServiceScopeFactory scopeFactory, string? imdbId, string sourceName)
    {
        if (OmdbCircuitBreaker.IsBlocked || string.IsNullOrEmpty(imdbId)) return null;

        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var apiKey = await settings.GetSettingAsync("omdb_imdb", "api_key") ?? await settings.GetSettingAsync("omdb_rotten_tomatoes", "api_key");
        if (string.IsNullOrEmpty(apiKey)) return null;

        var url = $"?i={imdbId}&apikey={apiKey}";
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("Response", out var res) && res.GetString() == "False")
        {
            if (root.TryGetProperty("Error", out var err) && err.GetString() == "Request limit reached!")
            {
                OmdbCircuitBreaker.BlockedUntil = DateTime.UtcNow.AddDays(1);
            }
            return null;
        }

        if (root.TryGetProperty("Ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Array)
        {
            foreach (var rating in ratings.EnumerateArray())
            {
                var source = rating.TryGetProperty("Source", out var s) ? s.GetString() : "";
                var value = rating.TryGetProperty("Value", out var v) ? v.GetString() : "";

                if (source == sourceName && !string.IsNullOrEmpty(value))
                {
                    return ParseRating(value);
                }
            }
        }

        if (sourceName == "Internet Movie Database" && root.TryGetProperty("imdbRating", out var imdbRating) && imdbRating.ValueKind != JsonValueKind.Null)
        {
            var val = imdbRating.GetString();
            if (val != "N/A" && !string.IsNullOrEmpty(val)) return ParseRating(val);
        }

        if (sourceName == "Metacritic" && root.TryGetProperty("Metascore", out var metaRating) && metaRating.ValueKind != JsonValueKind.Null)
        {
            var val = metaRating.GetString();
            if (val != "N/A" && !string.IsNullOrEmpty(val)) return ParseRating(val);
        }

        return null;
    }

    private static decimal? ParseRating(string value)
    {
        value = value.Replace("%", "").Split('/')[0].Trim();
        if (decimal.TryParse(value, out var result)) return result;
        return null;
    }
}
