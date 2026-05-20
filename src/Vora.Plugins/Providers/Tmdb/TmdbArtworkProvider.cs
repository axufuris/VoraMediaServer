using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tmdb;

public class TmdbArtworkProvider : IArtworkProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "tmdb_artwork";
    public string Name => "TMDB Artwork";
    public string Version => "1.0.0";
    public string Description => "Fetches high-quality posters and backdrops from The Movie Database.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public TmdbArtworkProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var apiKey = await settings.GetSettingAsync("tmdb_metadata", "api_key");

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(tmdbId))
            return new List<ArtworkResult>();

        string endpoint;
        if (mediaType.Equals("Collection", StringComparison.OrdinalIgnoreCase))
            endpoint = $"collection/{tmdbId}/images?api_key={apiKey}";
        else if (mediaType.Equals("TvShow", StringComparison.OrdinalIgnoreCase))
            endpoint = $"tv/{tmdbId}/images?api_key={apiKey}&include_image_language=en,null";
        else if (mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            endpoint = $"movie/{tmdbId}/images?api_key={apiKey}&include_image_language=en,null";
        else
            return new List<ArtworkResult>();

        var response = await _httpClient.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode) return new List<ArtworkResult>();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var results = new List<ArtworkResult>();

        if (root.TryGetProperty("posters", out var posters))
        {
            foreach (var p in posters.EnumerateArray())
            {
                results.Add(new ArtworkResult
                {
                    Kind = ArtworkKind.Poster,
                    Url = $"https://image.tmdb.org/t/p/original{p.GetProperty("file_path").GetString()}",
                    Language = p.TryGetProperty("iso_639_1", out var lang) && lang.ValueKind == JsonValueKind.String ? lang.GetString() : "None",
                    Width = p.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                    Height = p.TryGetProperty("height", out var h) ? h.GetInt32() : null,
                    VoteAverage = p.TryGetProperty("vote_average", out var va) ? va.GetDouble() : null
                });
            }
        }

        if (root.TryGetProperty("backdrops", out var backdrops))
        {
            foreach (var b in backdrops.EnumerateArray())
            {
                results.Add(new ArtworkResult
                {
                    Kind = ArtworkKind.Backdrop,
                    Url = $"https://image.tmdb.org/t/p/original{b.GetProperty("file_path").GetString()}",
                    Language = b.TryGetProperty("iso_639_1", out var lang) && lang.ValueKind == JsonValueKind.String ? lang.GetString() : "None",
                    Width = b.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                    Height = b.TryGetProperty("height", out var h) ? h.GetInt32() : null,
                    VoteAverage = b.TryGetProperty("vote_average", out var va) ? va.GetDouble() : null
                });
            }
        }

        return results.OrderByDescending(r => r.VoteAverage).ToList();
    }
}
