using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Trakt;

public class TraktCollectionSyncProvider : ICollectionSyncProvider
{
    private readonly HttpClient _httpClient;
    private readonly IPluginSettingsProvider _settings;

    public string Id => "trakt_collection_sync";
    public string Name => "Trakt.tv Lists";
    public string Version => "1.0.1";
    public string Description => "Auto-fills collections using public or private Trakt.tv lists.";
    public bool IsSystemPlugin => true;
    public string Type => "Collection_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ExternalIdLabel => "Trakt List ID or Slug";
    public string ExternalIdPlaceholder => "e.g., marvel-cinematic-universe";

    public TraktCollectionSyncProvider(HttpClient httpClient, IPluginSettingsProvider settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto { Key = "client_id", Label = "Trakt Client ID", Type = "password", Description = "Trakt API Client ID (free). Create a Trakt account at https://trakt.tv, then register a new application at https://trakt.tv/oauth/applications/new (any Name; set Redirect URI to 'urn:ietf:wg:oauth:2.0:oob'). After saving, copy the 'Client ID' value shown on the app page — the Client Secret is not needed. This same Client ID is also used by the Trakt chronology provider." }
        };
    }

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        var clientId = await _settings.GetSettingAsync(Id, "client_id");
        if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("Trakt Client ID is missing.");

        var listUrl = $"https://api.trakt.tv/lists/{externalId}";
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);
        listRequest.Headers.Add("trakt-api-version", "2");
        listRequest.Headers.Add("trakt-api-key", clientId);
        listRequest.Headers.Add("User-Agent", "VoraMediaServer/1.0");

        var listResponse = await _httpClient.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();

        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var root = listDoc.RootElement;

        string? username = null;
        if (root.TryGetProperty("user", out var userProp) && userProp.TryGetProperty("ids", out var userIdsProp) && userIdsProp.TryGetProperty("slug", out var userSlugProp))
        {
            username = userSlugProp.GetString();
        }

        string? listSlug = null;
        if (root.TryGetProperty("ids", out var idsProp) && idsProp.TryGetProperty("slug", out var slugProp))
        {
            listSlug = slugProp.GetString();
        }

        var itemsUrl = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(listSlug)
            ? $"https://api.trakt.tv/users/{username}/lists/{listSlug}/items"
            : $"https://api.trakt.tv/lists/{listSlug ?? externalId}/items";

        using var itemsRequest = new HttpRequestMessage(HttpMethod.Get, itemsUrl);
        itemsRequest.Headers.Add("trakt-api-version", "2");
        itemsRequest.Headers.Add("trakt-api-key", clientId);
        itemsRequest.Headers.Add("User-Agent", "VoraMediaServer/1.0");

        var itemsResponse = await _httpClient.SendAsync(itemsRequest);
        itemsResponse.EnsureSuccessStatusCode();

        var response = await itemsResponse.Content.ReadAsStringAsync();

        using var itemsDoc = JsonDocument.Parse(response);

        if (itemsDoc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Trakt API returned a {itemsDoc.RootElement.ValueKind} instead of an Array. URL: {itemsUrl}");
        }

        var results = new List<CollectionSyncItemDto>();

        foreach (var item in itemsDoc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeProp)) continue;
            var type = typeProp.GetString();

            if (string.IsNullOrEmpty(type) || !item.TryGetProperty(type, out var mediaEl)) continue;
            if (!mediaEl.TryGetProperty("ids", out var ids)) continue;

            results.Add(new CollectionSyncItemDto
            {
                TmdbId = ids.TryGetProperty("tmdb", out var tmdb) && tmdb.ValueKind == JsonValueKind.Number ? tmdb.GetInt32().ToString() : null,
                ImdbId = ids.TryGetProperty("imdb", out var imdb) && imdb.ValueKind == JsonValueKind.String ? imdb.GetString() : null,
                MediaType = type == "movie" ? "Movie" : "TvShow"
            });
        }

        return results;
    }
}
