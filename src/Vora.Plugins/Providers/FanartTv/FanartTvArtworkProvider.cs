using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.FanartTv;

public class FanartTvArtworkProvider : IArtworkProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "fanart_artwork";
    public string Name => "Fanart.tv Artwork";
    public string Version => "1.0.0";
    public string Description => "Fetches high-quality, textless posters and backdrops from Fanart.tv.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public FanartTvArtworkProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://webservice.fanart.tv/v3/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "Fanart.tv Project API Key",
                Type = "password",
                Description = "Your personal Project API Key from https://fanart.tv/get-an-api-key/"
            }
        };
    }

    public async Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null)
    {
        var results = new List<ArtworkResult>();

        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var apiKey = await settings.GetSettingAsync(Id, "api_key");

        if (string.IsNullOrEmpty(apiKey)) return results;

        string endpoint = "";

        if (mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tmdbId))
        {
            endpoint = $"movies/{tmdbId}?api_key={apiKey}";
        }
        else if (mediaType.Equals("TvShow", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(tvdbId))
        {
            endpoint = $"tv/{tvdbId}?api_key={apiKey}";
        }
        else
        {
            return results;
        }

        var response = await _httpClient.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode) return results;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        if (mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase))
        {
            ExtractArray(root, "movieposter", ArtworkKind.Poster, results);
            ExtractArray(root, "moviebackground", ArtworkKind.Backdrop, results);
            ExtractArray(root, "movielogo", ArtworkKind.Logo, results);
            ExtractArray(root, "hdmovielogo", ArtworkKind.Logo, results);
        }
        else
        {
            ExtractArray(root, "tvposter", ArtworkKind.Poster, results);
            ExtractArray(root, "showbackground", ArtworkKind.Backdrop, results);
            ExtractArray(root, "clearlogo", ArtworkKind.Logo, results);
            ExtractArray(root, "hdtvlogo", ArtworkKind.Logo, results);
        }

        return results.OrderByDescending(r => r.VoteAverage).ToList();
    }

    private static void ExtractArray(JsonElement root, string propertyName, ArtworkKind kind, List<ArtworkResult> results)
    {
        if (root.TryGetProperty(propertyName, out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                double voteAvg = 0;
                if (item.TryGetProperty("likes", out var likesProp))
                {
                    if (likesProp.ValueKind == JsonValueKind.String && double.TryParse(likesProp.GetString(), out var parsedLikes))
                    {
                        voteAvg = parsedLikes;
                    }
                    else if (likesProp.ValueKind == JsonValueKind.Number)
                    {
                        voteAvg = likesProp.GetDouble();
                    }
                }

                int? width = kind == ArtworkKind.Poster ? 1000 : 1920;
                int? height = kind == ArtworkKind.Poster ? 1426 : 1080;

                results.Add(new ArtworkResult
                {
                    Kind = kind,
                    Url = item.TryGetProperty("url", out var u) && u.ValueKind != JsonValueKind.Null ? u.GetString() ?? "" : "",
                    Language = item.TryGetProperty("lang", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetString() : "None",
                    VoteAverage = voteAvg,
                    Width = width,
                    Height = height
                });
            }
        }
    }
}
