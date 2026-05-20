using System.Net.Http;
using System.Text.RegularExpressions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Imdb;

public class ImdbChronologyProvider : IChronologyProvider
{
    private readonly HttpClient _httpClient;

    public string Id => "imdb_chronology";
    public string Name => "IMDB Community Lists";
    public string Version => "1.0.1";
    public string Description => "Fetches official and community-curated chronological timelines directly from IMDB.";
    public bool IsSystemPlugin => true;
    public string Type => "Chronology";
    public string ExternalIdLabel => "IMDb List ID";
    public string ExternalIdPlaceholder => "e.g., ls022528662";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<string> SupportedLibraryTypes => new[] { "Movie", "TvShow" };
    public bool IsEnabled => false;

    public ImdbChronologyProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string ProviderId => "imdb";
    public string ProviderName => "IMDB Custom Lists";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null)
    {
        if (string.IsNullOrWhiteSpace(externalId))
            throw new ArgumentException("IMDb requires an externalId (the IMDb List ID, e.g., ls022528662) to fetch the timeline.");

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

        var results = new List<ChronologyResult>();

        var matches = Regex.Matches(html, @"href=""/title/(tt\d+)/");
        var addedIds = new HashSet<string>();
        decimal position = 1;

        foreach (Match match in matches)
        {
            var imdbId = match.Groups[1].Value;

            if (addedIds.Add(imdbId))
            {
                results.Add(new ChronologyResult
                {
                    ImdbId = imdbId,
                    TmdbId = null,
                    MediaType = "Movie", // Defaulting to Movie; local DB match will sort it out
                    SortOrder = position++
                });
            }
        }

        return results;
    }
}
