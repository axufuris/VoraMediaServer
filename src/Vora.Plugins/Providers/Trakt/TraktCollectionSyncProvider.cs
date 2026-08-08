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

    public string ExternalIdLabel => "Trakt List URL, username/slug, or numeric ID";
    public string ExternalIdPlaceholder => "e.g., https://trakt.tv/users/yourname/lists/mcu-complete-chronologically";

    public TraktCollectionSyncProvider(HttpClient httpClient, IPluginSettingsProvider settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto { Key = "client_id", Label = "Trakt Client ID", Type = "password", Description = "Trakt API Client ID. Heads up: Trakt now limits free accounts to a single connected app and appears to require Trakt VIP to create new API applications, so this may not work on a free account (you'll see a 403). If you can create an app: register it at https://trakt.tv/oauth/applications/new (any Name; Redirect URI 'urn:ietf:wg:oauth:2.0:oob'), then copy the 'Client ID' (not the Client Secret). The same Client ID is used by the Trakt chronology provider. If you don't have Trakt VIP, use the free MDbList provider instead." }
        };
    }

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        var clientId = await _settings.GetSettingAsync(Id, "client_id");
        if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("Trakt Client ID is missing.");

        var itemsUrl = TraktListResolver.BuildItemsUrl(externalId);

        using var itemsRequest = new HttpRequestMessage(HttpMethod.Get, itemsUrl);
        itemsRequest.Headers.Add("trakt-api-version", "2");
        itemsRequest.Headers.Add("trakt-api-key", clientId);
        itemsRequest.Headers.Add("User-Agent", "VoraMediaServer/1.0");

        var itemsResponse = await _httpClient.SendAsync(itemsRequest);
        TraktListResolver.EnsureListResponse(itemsResponse);

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
