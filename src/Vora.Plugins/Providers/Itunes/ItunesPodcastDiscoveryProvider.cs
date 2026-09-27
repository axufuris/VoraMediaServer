using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Itunes;

public class ItunesPodcastDiscoveryProvider : IPodcastDiscoveryProvider
{
    public const string HttpClientName = "ItunesPodcastHttpClient";
    private const string SearchUrlTemplate = "https://itunes.apple.com/search?media=podcast&entity=podcast&term={0}&limit={1}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ItunesPodcastDiscoveryProvider> _logger;

    public string Id => "itunes_podcast_discovery";
    public string Name => "iTunes Podcast Discovery";
    public string ProviderName => "iTunes";
    public string Version => "1.0.0";
    public string Description => "Searches Apple's iTunes podcast directory. No API key required.";
    public bool IsSystemPlugin => true;
    public string Type => "PodcastDiscovery";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Podcast };

    public ItunesPodcastDiscoveryProvider(IHttpClientFactory httpClientFactory, ILogger<ItunesPodcastDiscoveryProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return Array.Empty<PluginSettingDefinitionDto>();
    }

    public async Task<IReadOnlyList<DiscoveredPodcast>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<DiscoveredPodcast>();

        var effectiveLimit = Math.Clamp(limit, 1, 200);
        var url = string.Format(SearchUrlTemplate, Uri.EscapeDataString(query.Trim()), effectiveLimit);

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DiscoveredPodcast>();
            }

            var list = new List<DiscoveredPodcast>(effectiveLimit);
            foreach (var item in results.EnumerateArray())
            {
                var entry = MapResult(item);
                if (entry != null) list.Add(entry);
            }
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iTunes podcast search failed for query {Query}", query);
            return Array.Empty<DiscoveredPodcast>();
        }
    }

    private DiscoveredPodcast? MapResult(JsonElement item)
    {
        var feedUrl = GetString(item, "feedUrl");
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;

        var title = GetString(item, "collectionName") ?? GetString(item, "trackName") ?? "Unknown Show";
        var author = GetString(item, "artistName");
        var artwork = GetString(item, "artworkUrl600")
            ?? GetString(item, "artworkUrl100")
            ?? GetString(item, "artworkUrl60");
        var homepage = GetString(item, "collectionViewUrl")
            ?? GetString(item, "trackViewUrl");

        return new DiscoveredPodcast
        {
            Title = title,
            Author = author,
            FeedUrl = feedUrl,
            ArtworkUrl = artwork,
            HomepageUrl = homepage,
            ProviderName = ProviderName
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop)) return null;
        if (prop.ValueKind != JsonValueKind.String) return null;
        var s = prop.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
