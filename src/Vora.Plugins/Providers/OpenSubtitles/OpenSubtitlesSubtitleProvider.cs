using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.OpenSubtitles;

public class OpenSubtitlesSubtitleProvider : ISubtitleSearchProvider, IPluginConnectionTest
{
    public const string HttpClientName = "OpenSubtitlesSearch";
    public const string PluginId = "opensubtitles_search";

    private const string ApiKeyHeader = "Api-Key";
    private const int MaxResults = 50;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // OpenSubtitles answers 429 with a Retry-After and, on the free tier, a hard
    // daily download quota. A rejected request is not retried into the same wall:
    // the provider goes quiet until the window it named has passed.
    private static DateTime? _blockedUntilUtc;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenSubtitlesSubtitleProvider> _logger;

    public string Id => PluginId;
    public string Name => "OpenSubtitles";
    public string Version => "1.0.0";
    public string Description => "Searches OpenSubtitles.com for subtitles and downloads them into the server's subtitle store.";
    public bool IsSystemPlugin => true;
    public string Type => "SubtitleSearch";
    public string DeveloperName => "Andy Xufuris";
    public string DocumentationUrl => "https://opensubtitles.stoplight.io/docs/opensubtitles-api";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public OpenSubtitlesSubtitleProvider(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<OpenSubtitlesSubtitleProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() =>
    [
        new PluginSettingDefinitionDto
        {
            Key = "api_key",
            Label = "OpenSubtitles API Key",
            Type = "password",
            Required = true,
            Placeholder = "Paste your OpenSubtitles API key",
            Description = "Create a free account at https://www.opensubtitles.com, then request an API key under Consumers. The free tier allows a limited number of downloads per day.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "username",
            Label = "Username",
            Type = "text",
            Required = false,
            Placeholder = "Optional",
            Description = "Only needed to download with your account's quota rather than the anonymous one. Leave blank to search without signing in.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "password",
            Label = "Password",
            Type = "password",
            Required = false,
            Description = "Used once to obtain a download token. Stored encrypted alongside the other plugin settings.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "default_languages",
            Label = "Default Languages",
            Type = "text",
            Required = false,
            Placeholder = "en,es",
            Description = "Comma-separated language codes searched when a request does not name any. Defaults to English.",
        },
    ];

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(await GetSettingAsync("api_key"));

    public async Task<IReadOnlyList<SubtitleSearchResultDto>> SearchAsync(SubtitleSearchQuery query, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetSettingAsync("api_key");
        if (string.IsNullOrWhiteSpace(apiKey)) return Array.Empty<SubtitleSearchResultDto>();
        if (IsRateLimited()) return Array.Empty<SubtitleSearchResultDto>();

        var languages = query.Languages.Count > 0
            ? query.Languages
            : ParseLanguages(await GetSettingAsync("default_languages"));

        var url = BuildSearchUrl(query, languages);

        try
        {
            using var client = CreateClient(apiKey!);
            using var response = await client.GetAsync(url, cancellationToken);

            if (!await EnsureNotRateLimitedAsync(response)) return Array.Empty<SubtitleSearchResultDto>();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenSubtitles search failed ({Status}) for {Url}.", response.StatusCode, url);
                return Array.Empty<SubtitleSearchResultDto>();
            }

            var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(Json, cancellationToken);
            if (payload?.Data == null) return Array.Empty<SubtitleSearchResultDto>();

            return payload.Data
                .Select(ToResult)
                .Where(r => r != null)
                .Select(r => r!)
                .Take(MaxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSubtitles search threw for {Url}.", url);
            return Array.Empty<SubtitleSearchResultDto>();
        }
    }

    public async Task<SubtitleDownloadDto?> DownloadAsync(string providerFileId, CancellationToken cancellationToken = default)
    {
        var apiKey = await GetSettingAsync("api_key");
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(providerFileId)) return null;
        if (IsRateLimited()) return null;

        try
        {
            using var client = CreateClient(apiKey!);

            // Two hops by design: /download mints a short-lived signed link that
            // counts against the quota, and the file itself comes from a CDN host
            // that must NOT be sent the API key.
            using var linkResponse = await client.PostAsJsonAsync("download", new { file_id = ParseFileId(providerFileId) }, cancellationToken);

            if (!await EnsureNotRateLimitedAsync(linkResponse)) return null;
            if (!linkResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenSubtitles refused a download link for {FileId} ({Status}).", providerFileId, linkResponse.StatusCode);
                return null;
            }

            var link = await linkResponse.Content.ReadFromJsonAsync<DownloadResponse>(Json, cancellationToken);
            if (link?.Link == null) return null;

            using var fileClient = _httpClientFactory.CreateClient(HttpClientName);
            var content = await fileClient.GetByteArrayAsync(link.Link, cancellationToken);
            if (content.Length == 0) return null;

            return new SubtitleDownloadDto
            {
                Content = content,
                Format = NormalizeFormat(link.FileName),
                FileName = link.FileName,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSubtitles download threw for {FileId}.", providerFileId);
            return null;
        }
    }

    public async Task<PluginConnectionTestResult> TestConnectionAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        if (!settings.TryGetValue("api_key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            return PluginConnectionTestResult.Fail("Enter an API key first.");
        }

        try
        {
            using var client = CreateClient(apiKey.Trim());
            using var response = await client.GetAsync("infos/languages", cancellationToken);

            if (response.IsSuccessStatusCode) return PluginConnectionTestResult.Ok("OpenSubtitles accepted the API key.");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return PluginConnectionTestResult.Fail("OpenSubtitles rejected the API key.");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return PluginConnectionTestResult.Fail("OpenSubtitles is rate-limiting this key right now. Try again shortly.");
            }

            return PluginConnectionTestResult.Fail($"OpenSubtitles returned {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return PluginConnectionTestResult.Fail($"Could not reach OpenSubtitles: {ex.Message}");
        }
    }

    public static string BuildSearchUrl(SubtitleSearchQuery query, IReadOnlyList<string> languages)
    {
        var parts = new List<string>();

        // An external id pins the exact title; without one the API falls back to
        // fuzzy title matching, which is where wrong-film subtitles come from.
        if (!string.IsNullOrWhiteSpace(query.ImdbId))
        {
            parts.Add($"imdb_id={Uri.EscapeDataString(query.ImdbId.TrimStart('t').TrimStart('0'))}");
        }
        else if (!string.IsNullOrWhiteSpace(query.TmdbId))
        {
            parts.Add($"tmdb_id={Uri.EscapeDataString(query.TmdbId)}");
        }
        else if (!string.IsNullOrWhiteSpace(query.Title))
        {
            parts.Add($"query={Uri.EscapeDataString(query.Title)}");
            if (query.Year.HasValue) parts.Add($"year={query.Year.Value}");
        }

        if (query.IsEpisode)
        {
            parts.Add($"season_number={query.Season!.Value}");
            parts.Add($"episode_number={query.Episode!.Value}");
        }

        if (languages.Count > 0)
        {
            parts.Add($"languages={Uri.EscapeDataString(string.Join(",", languages.Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0)))}");
        }

        return "subtitles?" + string.Join("&", parts);
    }

    public static IReadOnlyList<string> ParseLanguages(string? configured)
    {
        var parsed = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant())
            .Distinct()
            .ToList();

        return parsed.Count > 0 ? parsed : new List<string> { "en" };
    }

    public static string NormalizeFormat(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
        return extension is "srt" or "ass" or "ssa" or "vtt" or "sub" ? extension : "srt";
    }

    private static int ParseFileId(string providerFileId) =>
        int.TryParse(providerFileId, out var id) ? id : 0;

    private static SubtitleSearchResultDto? ToResult(SearchItem item)
    {
        var file = item.Attributes?.Files?.FirstOrDefault();
        if (file?.FileId == null) return null;

        return new SubtitleSearchResultDto
        {
            ProviderId = PluginId,
            ProviderFileId = file.FileId.Value.ToString(),
            ReleaseName = item.Attributes?.Release ?? file.FileName ?? "Subtitle",
            Language = item.Attributes?.Language,
            Format = NormalizeFormat(file.FileName),
            HearingImpaired = item.Attributes?.HearingImpaired ?? false,
            Forced = item.Attributes?.ForeignPartsOnly ?? false,
            DownloadCount = item.Attributes?.DownloadCount,
            Uploader = item.Attributes?.Uploader?.Name,
            Rating = item.Attributes?.Ratings,
        };
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress ??= new Uri("https://api.opensubtitles.com/api/v1/");
        client.DefaultRequestHeaders.Remove(ApiKeyHeader);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);
        return client;
    }

    private static bool IsRateLimited() => _blockedUntilUtc.HasValue && DateTime.UtcNow < _blockedUntilUtc.Value;

    private async Task<bool> EnsureNotRateLimitedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return true;

        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? (TimeSpan?)(date - DateTimeOffset.UtcNow) : null)
            ?? TimeSpan.FromMinutes(1);

        _blockedUntilUtc = DateTime.UtcNow + retryAfter;
        _logger.LogWarning("OpenSubtitles is rate-limiting; pausing requests until {Until:u}.", _blockedUntilUtc);

        await Task.CompletedTask;
        return false;
    }

    private async Task<string?> GetSettingAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync(PluginId, key);
    }

    private class SearchResponse
    {
        public List<SearchItem>? Data { get; set; }
    }

    private class SearchItem
    {
        public SearchAttributes? Attributes { get; set; }
    }

    private class SearchAttributes
    {
        public string? Language { get; set; }
        public string? Release { get; set; }

        [JsonPropertyName("hearing_impaired")]
        public bool? HearingImpaired { get; set; }

        [JsonPropertyName("foreign_parts_only")]
        public bool? ForeignPartsOnly { get; set; }

        [JsonPropertyName("download_count")]
        public int? DownloadCount { get; set; }

        public decimal? Ratings { get; set; }
        public UploaderInfo? Uploader { get; set; }
        public List<SubtitleFile>? Files { get; set; }
    }

    private class UploaderInfo
    {
        public string? Name { get; set; }
    }

    private class SubtitleFile
    {
        [JsonPropertyName("file_id")]
        public int? FileId { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }

    private class DownloadResponse
    {
        public string? Link { get; set; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }
    }
}
