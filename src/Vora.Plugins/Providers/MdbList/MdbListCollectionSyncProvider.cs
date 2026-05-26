using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MdbList;

public class MdbListCollectionSyncProvider : ICollectionSyncProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginSettingsProvider _settings;

    public string Id => "mdblist_collection_sync";
    public string Name => "MDbList.com";
    public string Version => "1.0.0";
    public string Description => "Auto-fills collections using the MDbList API (bypassing IMDb bot protections).";
    public bool IsSystemPlugin => true;
    public string Type => "Collection_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ExternalIdLabel => "MDbList URL";
    public string ExternalIdPlaceholder => "e.g., https://mdblist.com/lists/hdlists/latest-hd-family-movies-top-rated-from-1980-to-today";

    public MdbListCollectionSyncProvider(IHttpClientFactory httpClientFactory, IPluginSettingsProvider settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto { Key = "api_key", Label = "MDbList API Key", Type = "password", Description = "MDbList API Key (free). Sign in at https://mdblist.com, then open Preferences → API and click 'Generate API Key' (https://mdblist.com/preferences). Free tier provides ample quota for personal use. This same key is reused by the MDbList chronology provider." }
        };
    }

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        var apiKey = await _settings.GetSettingAsync(Id, "api_key");
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("MDbList API Key is missing. Please configure it in the plugin settings.");

        externalId = externalId.Trim();
        if (externalId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(externalId);
            externalId = uri.AbsolutePath.Replace("/lists/", "", StringComparison.OrdinalIgnoreCase).Trim('/');
        }

        if (!externalId.Contains('/') && !long.TryParse(externalId, out _))
        {
            throw new InvalidOperationException($"Invalid MDbList ID: '{externalId}'. You must provide either a numeric ID, 'username/slug', or paste the full MDbList URL.");
        }

        var client = _httpClientFactory.CreateClient();
        var results = new List<CollectionSyncItemDto>();
        string? nextCursor = null;

        do
        {
            var url = $"https://api.mdblist.com/lists/{externalId}/items?apikey={apiKey}";
            if (!string.IsNullOrEmpty(nextCursor)) url += $"&next_cursor={nextCursor}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "VoraMediaServer/1.0");

            var response = await client.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"MDbList could not find a list matching '{externalId}'. Please verify the URL or username/slug.");
            }

            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            var elementsToProcess = new List<JsonElement>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                elementsToProcess.AddRange(root.EnumerateArray());
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("movies", out var moviesProp) && moviesProp.ValueKind == JsonValueKind.Array)
                    elementsToProcess.AddRange(moviesProp.EnumerateArray());

                if (root.TryGetProperty("shows", out var showsProp) && showsProp.ValueKind == JsonValueKind.Array)
                    elementsToProcess.AddRange(showsProp.EnumerateArray());

                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                    elementsToProcess.AddRange(dataProp.EnumerateArray());

                if (root.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                    elementsToProcess.AddRange(itemsProp.EnumerateArray());

                if (root.TryGetProperty("next_cursor", out var cursorProp) && cursorProp.ValueKind == JsonValueKind.String)
                    nextCursor = cursorProp.GetString();
                else
                    nextCursor = null;
            }

            if (!elementsToProcess.Any() && root.ValueKind != JsonValueKind.Array && root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"MDbList API returned an unexpected JSON structure. URL: {url}");
            }

            foreach (var item in elementsToProcess)
            {
                string? imdbId = null;
                string? tmdbId = null;
                string mediaType = "Movie";

                if (item.TryGetProperty("mediatype", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                {
                    var typeStr = typeProp.GetString()?.ToLower() ?? "";
                    mediaType = typeStr.Contains("show") || typeStr.Contains("tv") ? "TvShow" : "Movie";
                }

                if (item.TryGetProperty("ids", out var idsProp) && idsProp.ValueKind == JsonValueKind.Object)
                {
                    if (idsProp.TryGetProperty("imdb", out var imdbObj) && imdbObj.ValueKind == JsonValueKind.String)
                        imdbId = imdbObj.GetString();

                    if (idsProp.TryGetProperty("tmdb", out var tmdbObj))
                        tmdbId = tmdbObj.ToString(); // safely handles both numbers and strings
                }

                if (string.IsNullOrEmpty(imdbId) && item.TryGetProperty("imdb_id", out var rootImdb) && rootImdb.ValueKind == JsonValueKind.String)
                    imdbId = rootImdb.GetString();

                if (string.IsNullOrEmpty(tmdbId) && item.TryGetProperty("tmdb_id", out var rootTmdb))
                    tmdbId = rootTmdb.ToString();

                if (!string.IsNullOrEmpty(imdbId) || !string.IsNullOrEmpty(tmdbId))
                {
                    results.Add(new CollectionSyncItemDto
                    {
                        ImdbId = imdbId,
                        TmdbId = tmdbId,
                        MediaType = mediaType
                    });
                }
            }

        } while (!string.IsNullOrEmpty(nextCursor));

        return results;
    }
}
