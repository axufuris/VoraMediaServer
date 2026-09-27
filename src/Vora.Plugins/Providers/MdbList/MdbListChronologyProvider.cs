using System.Net.Http;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.MdbList;

public class MdbListChronologyProvider : IChronologyProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginSettingsProvider _settings;

    public string Id => "mdblist_chronology";
    public string Name => "MDbList.com Timelines";
    public string Version => "1.0.0";
    public string Description => "Fetches chronological timelines directly from MDbList APIs.";
    public bool IsSystemPlugin => true;
    public string Type => "Chronology";
    public string ExternalIdLabel => "MDbList URL";
    public string ExternalIdPlaceholder => "e.g., https://mdblist.com/lists/hdlists/latest-hd-family-movies-top-rated-from-1980-to-today";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public MdbListChronologyProvider(IHttpClientFactory httpClientFactory, IPluginSettingsProvider settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public string ProviderId => "mdblist";
    public string ProviderName => "MDbList.com Timelines";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>();
    }

    public async Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("MDbList requires an externalId to fetch the timeline.");

        var apiKey = await _settings.GetSettingAsync("mdblist_collection_sync", "api_key");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("MDbList API Key is missing. Please configure it in the plugin settings.");

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
        var rawItems = new List<(string? ImdbId, string? TmdbId, string MediaType, decimal Rank, bool HasRank)>();
        string? nextCursor = null;

        do
        {
            var url = $"https://api.mdblist.com/lists/{externalId}/items?apikey={apiKey}";
            if (!string.IsNullOrEmpty(nextCursor)) url += $"&next_cursor={nextCursor}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "VoraMediaServer/1.0");

            var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"MDbList could not find a list matching '{externalId}'. Please verify the URL or username/slug.");
            }

            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
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

                decimal rank = 0;
                bool hasRank = false;
                if (item.TryGetProperty("rank", out var rankProp) && rankProp.ValueKind == JsonValueKind.Number && rankProp.TryGetDecimal(out var rankVal))
                {
                    rank = rankVal;
                    hasRank = true;
                }

                if (!string.IsNullOrEmpty(imdbId) || !string.IsNullOrEmpty(tmdbId))
                {
                    rawItems.Add((imdbId, tmdbId, mediaType, rank, hasRank));
                }
            }

        } while (!string.IsNullOrEmpty(nextCursor));

        return BuildOrderedResults(rawItems);
    }

    // MDbList splits a list into per-type arrays (movies, shows, …), each already
    // in the list's display order, plus a global "rank" per item. Concatenating
    // the arrays would dump every show after every movie, so instead keep the
    // movie array in its display order (rank can be stale when an item was
    // re-added) and interleave the other types into it by rank.
    private static List<ChronologyResult> BuildOrderedResults(List<(string? ImdbId, string? TmdbId, string MediaType, decimal Rank, bool HasRank)> items)
    {
        static ChronologyResult ToResult((string? ImdbId, string? TmdbId, string MediaType, decimal Rank, bool HasRank) it)
            => new() { ImdbId = it.ImdbId, TmdbId = it.TmdbId, MediaType = it.MediaType };

        if (items.Count == 0) return new List<ChronologyResult>();

        if (items.All(i => !i.HasRank))
        {
            decimal p = 1;
            return items.Select(i => { var r = ToResult(i); r.SortOrder = p++; return r; }).ToList();
        }

        var movies = items.Where(i => i.MediaType == "Movie").ToList();
        var others = items.Where(i => i.MediaType != "Movie").ToList();

        if (movies.Count == 0)
        {
            decimal p = 1;
            return items.OrderBy(i => i.Rank).Select(i => { var r = ToResult(i); r.SortOrder = p++; return r; }).ToList();
        }

        var maxRank = items.Max(i => i.Rank) + 1m;
        var keyed = new List<(decimal Key, ChronologyResult Result)>();

        for (var i = 0; i < movies.Count; i++)
        {
            keyed.Add((i, ToResult(movies[i])));
        }

        foreach (var other in others)
        {
            var lastSmaller = -1;
            for (var i = 0; i < movies.Count; i++)
            {
                if (movies[i].Rank < other.Rank) lastSmaller = i;
            }
            keyed.Add((lastSmaller + (other.Rank / maxRank), ToResult(other)));
        }

        keyed.Sort((a, b) => a.Key.CompareTo(b.Key));

        decimal pos = 1;
        foreach (var k in keyed)
        {
            k.Result.SortOrder = pos++;
        }
        return keyed.Select(k => k.Result).ToList();
    }
}
