using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Genius;

public class GeniusLyricsProvider : ILyricsProvider, IPluginConnectionTest
{
    public const string HttpClientName = "GeniusHttpClient";
    private const string SearchUrlTemplate = "https://api.genius.com/search?q={0}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GeniusLyricsProvider> _logger;

    public string Id => "genius_lyrics";
    public string Name => "Genius Lyrics";
    public string ProviderName => "Genius";
    public string Version => "1.0.0";
    public string Description => "Fetches plain lyrics from Genius. Requires an API access token from https://genius.com/api-clients. Used as a fallback when LRClib has no result.";
    public bool IsSystemPlugin => true;
    public string Type => "Lyrics";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public GeniusLyricsProvider(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, ILogger<GeniusLyricsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions()
    {
        return new List<PluginSettingDefinitionDto>
        {
            new PluginSettingDefinitionDto
            {
                Key = "access_token",
                Label = "Genius API Access Token",
                Type = "password",
                DefaultValue = string.Empty,
                Required = true,
                Placeholder = "Paste your Genius access token",
                Description = "Genius Client Access Token (free). Sign in at https://genius.com, then create an API client at https://genius.com/api-clients (any App Name and Website URL — Genius does not validate them). On the resulting client page click 'Generate Access Token' and paste that value here. Note: Genius lyrics come back as plain text only — no time-synced LRC."
            }
        };
    }

    public async Task<PluginConnectionTestResult> TestConnectionAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        if (!settings.TryGetValue("access_token", out var token) || string.IsNullOrWhiteSpace(token))
        {
            return PluginConnectionTestResult.Fail("Enter an access token first.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.genius.com/search?q=test");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return PluginConnectionTestResult.Ok("Genius accepted the access token.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return PluginConnectionTestResult.Fail("Genius rejected the access token.");
        }
        return PluginConnectionTestResult.Fail($"Unexpected response from Genius (HTTP {(int)response.StatusCode}).");
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var token = await settings.GetSettingAsync(Id, "access_token");
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public async Task<LyricsResult?> GetLyricsAsync(string artistName, string trackTitle, string? albumTitle, int? durationSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName) || string.IsNullOrWhiteSpace(trackTitle)) return null;
        var accessToken = await GetAccessTokenAsync();
        if (accessToken == null) return null;

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var query = NormalizeQuery($"{artistName} {trackTitle}");
            var searchUrl = string.Format(SearchUrlTemplate, Uri.EscapeDataString(query));

            using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            searchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var searchResponse = await client.SendAsync(searchRequest, cancellationToken);
            if (!searchResponse.IsSuccessStatusCode) return null;

            var searchJson = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(searchJson);
            if (!doc.RootElement.TryGetProperty("response", out var resp)) return null;
            if (!resp.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array) return null;

            string? pageUrl = null;
            foreach (var hit in hits.EnumerateArray())
            {
                if (!hit.TryGetProperty("result", out var result)) continue;
                var hitArtist = result.TryGetProperty("primary_artist", out var pa) && pa.TryGetProperty("name", out var pn) ? pn.GetString() : null;
                var hitTitle = result.TryGetProperty("title", out var ti) ? ti.GetString() : null;
                if (!IsLikelyMatch(artistName, trackTitle, hitArtist, hitTitle)) continue;
                if (result.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                {
                    pageUrl = url.GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(pageUrl)) return null;

            using var pageResponse = await client.GetAsync(pageUrl, cancellationToken);
            if (!pageResponse.IsSuccessStatusCode) return null;
            var html = await pageResponse.Content.ReadAsStringAsync(cancellationToken);

            var lyrics = ExtractLyricsFromHtml(html);
            if (string.IsNullOrWhiteSpace(lyrics)) return null;

            return new LyricsResult
            {
                PlainLyrics = lyrics,
                SyncedLyrics = null,
                ProviderName = ProviderName,
                SourceUrl = pageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Genius lookup failed for {Artist} / {Track}", artistName, trackTitle);
            return null;
        }
    }

    private static string NormalizeQuery(string input)
    {
        var trimmed = Regex.Replace(input, @"\s*[\(\[][^)\]]*[\)\]]\s*", " ");
        trimmed = Regex.Replace(trimmed, @"\bfeat\.?\b.*$", "", RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\s+", " ").Trim();
        return trimmed;
    }

    private static bool IsLikelyMatch(string queryArtist, string queryTitle, string? hitArtist, string? hitTitle)
    {
        if (string.IsNullOrWhiteSpace(hitArtist) || string.IsNullOrWhiteSpace(hitTitle)) return false;
        return ContainsLoose(hitArtist, queryArtist) || ContainsLoose(queryArtist, hitArtist)
            ? (ContainsLoose(hitTitle, queryTitle) || ContainsLoose(queryTitle, hitTitle))
            : false;
    }

    private static bool ContainsLoose(string haystack, string needle)
    {
        var h = Regex.Replace(haystack.ToLowerInvariant(), @"[^a-z0-9]", "");
        var n = Regex.Replace(needle.ToLowerInvariant(), @"[^a-z0-9]", "");
        return h.Length > 0 && n.Length > 0 && h.Contains(n);
    }

    private static readonly Regex LyricsContainerRegex = new(
        @"<div[^>]*data-lyrics-container=""true""[^>]*>(?<body>.*?)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex BrRegex = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static string? ExtractLyricsFromHtml(string html)
    {
        var matches = LyricsContainerRegex.Matches(html);
        if (matches.Count == 0) return null;

        var pieces = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var body = m.Groups["body"].Value;
            var withBreaks = BrRegex.Replace(body, "\n");
            var stripped = TagRegex.Replace(withBreaks, string.Empty);
            var decoded = WebUtility.HtmlDecode(stripped);
            pieces.Add(decoded.Trim());
        }
        var combined = string.Join("\n\n", pieces).Trim();
        combined = Regex.Replace(combined, "\n{3,}", "\n\n");
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }
}
