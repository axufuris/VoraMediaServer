using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MyAnimeList;

public class MyAnimeListArtworkProvider : IArtworkProvider
{
    private readonly HttpClient _httpClient;
    private readonly IServiceScopeFactory _scopeFactory;

    public string Id => "mal_artwork";
    public string Name => "MyAnimeList Artwork";
    public string Version => "1.0.0";
    public string Description => "Fetches official anime posters and key visuals from MyAnimeList.";
    public bool IsSystemPlugin => true;
    public string Type => "Artwork";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };

    public MyAnimeListArtworkProvider(HttpClient httpClient, IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _scopeFactory = scopeFactory;
        _httpClient.BaseAddress = new Uri("https://api.myanimelist.net/v2/");
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "client_id",
                Label = "MAL Client ID",
                Type = "text",
                Description = "MyAnimeList API Client ID (free). Sign in at https://myanimelist.net, then create an API client at https://myanimelist.net/apiconfig (App Type: 'Web', App Purpose: 'Other'). Copy the Client ID shown after saving — the Client Secret is not needed. This single Client ID also powers the MAL Discovery rows."
            }
        };
    }

    public async Task<IEnumerable<ArtworkResult>> GetArtworkAsync(string? tmdbId, string? tvdbId, string? imdbId, string mediaType, string? localPath = null, string? title = null)
    {
        var results = new List<ArtworkResult>();

        if (string.IsNullOrEmpty(title)) return results;

        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var clientId = await settings.GetSettingAsync(Id, "client_id");

        if (string.IsNullOrEmpty(clientId)) return results;

        var endpoint = $"anime?q={Uri.EscapeDataString(title)}&limit=1&fields=pictures,mean";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("X-MAL-CLIENT-ID", clientId);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return results;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array && dataArray.GetArrayLength() > 0)
        {
            var animeNode = dataArray[0].GetProperty("node");
            var malScore = animeNode.TryGetProperty("mean", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetDouble() : 0;

            if (animeNode.TryGetProperty("main_picture", out var mainPic) && mainPic.ValueKind != JsonValueKind.Null)
            {
                if (mainPic.TryGetProperty("large", out var largeUrl))
                {
                    results.Add(new ArtworkResult
                    {
                        Kind = ArtworkKind.Poster,
                        Url = largeUrl.GetString() ?? "",
                        Language = "jp",
                        VoteAverage = malScore
                    });
                }
            }

            if (animeNode.TryGetProperty("pictures", out var pictures) && pictures.ValueKind == JsonValueKind.Array)
            {
                foreach (var pic in pictures.EnumerateArray())
                {
                    if (pic.TryGetProperty("large", out var pLarge))
                    {
                        var url = pLarge.GetString();
                        if (!results.Any(r => r.Url == url))
                        {
                            results.Add(new ArtworkResult
                            {
                                Kind = ArtworkKind.Poster,
                                Url = url ?? "",
                                Language = "jp",
                                VoteAverage = malScore - 0.1
                            });
                        }
                    }
                }
            }
        }

        return results;
    }
}
