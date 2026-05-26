using System.Net.Http;
using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Imdb;

public class ImdbCollectionSyncProvider : ICollectionSyncProvider
{
    private readonly HttpClient _httpClient;

    public string Id => "imdb_collection_sync";
    public string Name => "IMDb Public Lists";
    public string Version => "1.0.1";
    public string Description => "Auto-fills collections using public IMDb user lists.";
    public bool IsSystemPlugin => true;
    public string Type => "Collection_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ExternalIdLabel => "IMDb List ID";
    public string ExternalIdPlaceholder => "e.g., ls022528662";
    public bool IsEnabled => false;

    public ImdbCollectionSyncProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        var requestUrl = $"https://www.imdb.com/list/{externalId}/";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("Sec-Ch-Ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
        request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
        request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
        request.Headers.Add("Sec-Fetch-Dest", "document");
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Headers.Add("Sec-Fetch-Site", "none");
        request.Headers.Add("Sec-Fetch-User", "?1");
        request.Headers.Add("Upgrade-Insecure-Requests", "1");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        if (html.Contains("awsWafCookieDomainList") || html.Contains("challenge.js"))
        {
            throw new InvalidOperationException("IMDb actively blocked the request via AWS WAF. Direct scraping is currently blocked for this IP address.");
        }

        var results = new List<CollectionSyncItemDto>();

        var matches = Regex.Matches(html, @"href=""/title/(tt\d+)/");
        var addedIds = new HashSet<string>();

        foreach (Match match in matches)
        {
            var imdbId = match.Groups[1].Value;
            if (addedIds.Add(imdbId))
            {
                results.Add(new CollectionSyncItemDto
                {
                    ImdbId = imdbId,
                    MediaType = "Movie"
                });
            }
        }

        return results;
    }
}
