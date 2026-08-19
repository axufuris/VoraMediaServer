using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MediUX;

public class MediUxArtworkProvider : IArtworkProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "mediux_artwork";
    public string Name => "MediUX Artwork";
    public string Version => "1.0.0";
    public string Description => "Fetches matching, community-driven poster sets and title cards from MediUX.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public MediUxArtworkProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api.mediux.pro/v1/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "api_key",
                Label = "MediUX API Key",
                Type = "password",
                Required = true,
                Placeholder = "Paste your MediUX API key",
                Description = "MediUX API Key. Sign in at https://mediux.pro and copy the API key from the account settings page (https://mediux.pro/settings). Free tier available; rate limits apply per account."
            }
        };
    }

    public async Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null, CancellationToken cancellationToken = default)
    {
        var results = new List<ArtworkResult>();

        if (string.IsNullOrEmpty(tmdbId)) return results;

        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var apiKey = await settings.GetSettingAsync(Id, "api_key");

        if (string.IsNullOrEmpty(apiKey)) return results;

        string showType = "movie";
        if (mediaType.Equals("TvShow", StringComparison.OrdinalIgnoreCase)) showType = "show";
        else if (mediaType.Equals("Collection", StringComparison.OrdinalIgnoreCase)) showType = "collection";

        var endpoint = $"assets?tmdb_id={tmdbId}&type={showType}";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return results;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    string typeStr = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                    ArtworkKind? kind = null;
                    if (typeStr.Contains("poster", StringComparison.OrdinalIgnoreCase)) kind = ArtworkKind.Poster;
                    else if (typeStr.Contains("backdrop", StringComparison.OrdinalIgnoreCase) || typeStr.Contains("background", StringComparison.OrdinalIgnoreCase)) kind = ArtworkKind.Backdrop;

                    if (kind.HasValue)
                    {
                        results.Add(new ArtworkResult
                        {
                            Kind = kind.Value,
                            Url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                            Language = item.TryGetProperty("language", out var l) ? l.GetString() : "None",
                            VoteAverage = item.TryGetProperty("downloads", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0
                        });
                    }
                }
            }
        }
        catch
        {
        }

        return results.OrderByDescending(r => r.VoteAverage).ToList();
    }
}
