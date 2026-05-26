using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Tvdb;

public class TvdbArtworkProvider : IArtworkProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "tvdb_artwork";
    public string Name => "TVDB Artwork";
    public string Version => "1.0.0";
    public string Description => "Fetches high-quality posters and backdrops from TVDB.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public TvdbArtworkProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api4.thetvdb.com/v4/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    private async Task<string?> GetValidTokenAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();

        var token = await settings.GetSettingAsync("tvdb_metadata", "tvdb_token");
        if (string.IsNullOrEmpty(token))
        {
            var apiKey = await settings.GetSettingAsync("tvdb_metadata", "api_key");
            if (string.IsNullOrEmpty(apiKey)) return null;

            var loginRequest = new { apikey = apiKey };
            var content = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("login", content);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
                if (token != null) await settings.SetSettingAsync("tvdb_metadata", "tvdb_token", token);
            }
        }
        return token;
    }

    public async Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tvdbId)) return new List<ArtworkResult>();

        var token = await GetValidTokenAsync();
        if (string.IsNullOrEmpty(token)) return new List<ArtworkResult>();

        string endpoint;
        if (mediaType.Equals("TvShow", StringComparison.OrdinalIgnoreCase))
            endpoint = $"series/{tvdbId}/extended";
        else if (mediaType.Equals("Season", StringComparison.OrdinalIgnoreCase))
            endpoint = $"seasons/{tvdbId}/extended";
        else if (mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase))
            endpoint = $"movies/{tvdbId}/extended";
        else
            return new List<ArtworkResult>();

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new List<ArtworkResult>();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = new List<ArtworkResult>();

        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("artworks", out var artworks))
        {
            foreach (var art in artworks.EnumerateArray())
            {
                var typeId = art.TryGetProperty("type", out var t) ? t.GetInt32() : 0;

                ArtworkKind? kind = null;
                if (typeId == 2 || typeId == 14) kind = ArtworkKind.Poster;
                else if (typeId == 3 || typeId == 15) kind = ArtworkKind.Backdrop;

                if (kind.HasValue)
                {
                    results.Add(new ArtworkResult
                    {
                        Kind = kind.Value,
                        Url = art.GetProperty("image").GetString() ?? "",
                        Language = art.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String ? lang.GetString()?.ToUpper() : "None",
                        Width = art.TryGetProperty("width", out var w) ? w.GetInt32() : null,
                        Height = art.TryGetProperty("height", out var h) ? h.GetInt32() : null,
                        VoteAverage = art.TryGetProperty("score", out var s) ? s.GetDouble() : null
                    });
                }
            }
        }

        return results.OrderByDescending(r => r.VoteAverage).ToList();
    }
}
