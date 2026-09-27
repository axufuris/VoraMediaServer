using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tmdb;

public class TmdbRatingsProvider : IRatingsProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "tmdb_rating";
    public string Name => "TMDB Ratings";
    public string Version => "1.0.0";
    public string Description => "Fetches the TMDB user rating (vote average) for movies and shows. Shares the TMDB metadata plugin's API key and has no daily request quota.";
    public bool IsSystemPlugin => true;
    public string Type => "Ratings";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string RatingSourceName => "TMDB";

    public TmdbRatingsProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
        }
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<decimal?> FetchRatingAsync(string? imdbId, string? tmdbId, string? tvdbId, string mediaType, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync();
        if (string.IsNullOrEmpty(apiKey)) return null;

        var isTv = mediaType is "TvShow" or "Season" or "Episode";

        if (!string.IsNullOrEmpty(tmdbId))
        {
            var endpoint = isTv ? $"tv/{tmdbId}" : $"movie/{tmdbId}";
            var rating = await FetchVoteAverageAsync($"{endpoint}?api_key={apiKey}", cancellationToken);
            if (rating.HasValue) return rating;
        }

        var externalSource = !string.IsNullOrEmpty(imdbId) ? "imdb_id"
            : !string.IsNullOrEmpty(tvdbId) ? "tvdb_id"
            : null;
        var externalId = !string.IsNullOrEmpty(imdbId) ? imdbId : tvdbId;

        if (externalSource != null && !string.IsNullOrEmpty(externalId))
        {
            var arrayName = isTv ? "tv_results" : "movie_results";
            return await FetchVoteAverageFromFindAsync($"find/{externalId}?api_key={apiKey}&external_source={externalSource}", arrayName, cancellationToken);
        }

        return null;
    }

    private async Task<decimal?> FetchVoteAverageAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return ReadVoteAverage(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private async Task<decimal?> FetchVoteAverageFromFindAsync(string url, string arrayName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (doc.RootElement.TryGetProperty(arrayName, out var results) && results.ValueKind == JsonValueKind.Array)
            {
                var first = results.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object) return ReadVoteAverage(first);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ReadVoteAverage(JsonElement element)
    {
        if (element.TryGetProperty("vote_average", out var va)
            && va.ValueKind == JsonValueKind.Number
            && va.TryGetDecimal(out var rating)
            && rating > 0)
        {
            return rating;
        }
        return null;
    }

    private async Task<string?> GetApiKeyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync("tmdb_metadata", "api_key");
    }
}
