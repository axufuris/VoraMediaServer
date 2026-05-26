using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Trakt;

public class TraktChronologyProvider : IChronologyProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginSettingsProvider _settings;

    public string Id => "trakt_chronology";
    public string Name => "Trakt.tv Community Lists";
    public string Version => "1.0.1";
    public string Description => "Fetches official and community-curated chronological timelines directly from Trakt.tv.";
    public bool IsSystemPlugin => true;
    public string Type => "Chronology";
    public string ExternalIdLabel => "Trakt List ID or Slug";
    public string ExternalIdPlaceholder => "e.g., marvel-cinematic-universe";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public TraktChronologyProvider(IHttpClientFactory httpClientFactory, IPluginSettingsProvider settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public string ProviderId => "trakt";
    public string ProviderName => "Trakt.tv Community Lists";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("Trakt requires an externalId (the Trakt List ID) to fetch the timeline.");

        var clientId = await _settings.GetSettingAsync(Id, "client_id");

        if (string.IsNullOrEmpty(clientId))
        {
            clientId = await _settings.GetSettingAsync("trakt_collection_sync", "client_id");
        }

        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Trakt Client ID is missing. Please configure it in the plugin settings.");

        var client = _httpClientFactory.CreateClient();

        var listUrl = $"https://api.trakt.tv/lists/{externalId}";
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);
        listRequest.Headers.Add("trakt-api-version", "2");
        listRequest.Headers.Add("trakt-api-key", clientId);
        listRequest.Headers.Add("User-Agent", "VoraMediaServer/1.0");

        var listResponse = await client.SendAsync(listRequest, cancellationToken);
        listResponse.EnsureSuccessStatusCode();

        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
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

        var itemsResponse = await client.SendAsync(itemsRequest, cancellationToken);
        itemsResponse.EnsureSuccessStatusCode();

        using var stream = await itemsResponse.Content.ReadAsStreamAsync(cancellationToken);

        List<TraktListItem>? items = null;
        try
        {
            items = await JsonSerializer.DeserializeAsync<List<TraktListItem>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Trakt API returned an unexpected JSON structure instead of an Array. URL: {itemsUrl}", ex);
        }

        var results = new List<ChronologyResult>();
        if (items == null) return results;

        foreach (var item in items)
        {
            string? tmdbId = item.Type == "movie" ? item.Movie?.Ids?.Tmdb?.ToString() : item.Show?.Ids?.Tmdb?.ToString();
            string? imdbId = item.Type == "movie" ? item.Movie?.Ids?.Imdb : item.Show?.Ids?.Imdb;

            if (!string.IsNullOrEmpty(tmdbId) || !string.IsNullOrEmpty(imdbId))
            {
                results.Add(new ChronologyResult
                {
                    TmdbId = tmdbId,
                    ImdbId = imdbId,
                    MediaType = item.Type == "movie" ? "Movie" : "TvShow",
                    SortOrder = item.Rank
                });
            }
        }

        return results;
    }

    private class TraktListItem
    {
        public decimal Rank { get; set; }
        public string Type { get; set; } = string.Empty;
        public TraktMediaItem? Movie { get; set; }
        public TraktMediaItem? Show { get; set; }
    }

    private class TraktMediaItem
    {
        public TraktIds? Ids { get; set; }
    }

    private class TraktIds
    {
        public int? Tmdb { get; set; }
        public string? Imdb { get; set; }
    }
}
