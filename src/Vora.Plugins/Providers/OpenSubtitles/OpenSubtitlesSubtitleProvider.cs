using System.Net;
using System.Net.Http.Headers;
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
    public const string DefaultBaseUrl = "https://api.opensubtitles.com/api/v1/";

    private const string ApiKeyHeader = "Api-Key";
    private const int MaxResults = 50;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim LoginGate = new(1, 1);

    // The API rejects a request that carries no distinctive User-Agent, and asks
    // that each consumer send its own.
    private static readonly ProductInfoHeaderValue UserAgent = new("Vora", "1.0");

    private static OpenSubtitlesSession? _session;
    private static DateTime? _blockedUntilUtc;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OpenSubtitlesSubtitleProvider> _logger;

    public string Id => PluginId;
    public string Name => "OpenSubtitles";
    public string Version => "1.1.0";
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
            Description = "Required for both authentication modes. Create a free account at https://www.opensubtitles.com, then request an API key under Consumers.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "auth_mode",
            Label = "Authentication Mode",
            Type = "select",
            Required = false,
            Options = [OpenSubtitlesAuthPlan.ApiKeyOnlyLabel, OpenSubtitlesAuthPlan.AccountLabel],
            Description = "API key only downloads against the shared anonymous quota. Account signs in with your OpenSubtitles login so downloads count against your own — higher on a free account, higher still on VIP.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "username",
            Label = "Username",
            Type = "text",
            Required = false,
            Placeholder = "Only for Account mode",
            Description = "Your OpenSubtitles username. Ignored in API-key-only mode.",
        },
        new PluginSettingDefinitionDto
        {
            Key = "password",
            Label = "Password",
            Type = "password",
            Required = false,
            Description = "Your OpenSubtitles password, used only to obtain a session token. Ignored in API-key-only mode.",
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
            using var response = await SendAsync(apiKey!, HttpMethod.Get, url, content: null, cancellationToken);
            if (response == null) return Array.Empty<SubtitleSearchResultDto>();

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
        catch (SubtitleProviderException)
        {
            throw;
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

        if (IsRateLimited())
        {
            throw new SubtitleProviderException(
                "OpenSubtitles is rate-limiting this server. Try again in a few minutes.",
                isQuotaExhausted: false,
                retryAfterUtc: _blockedUntilUtc);
        }

        // Two hops by design: /download mints a short-lived signed link that
        // counts against the quota, and the file itself comes from a CDN host
        // that must NOT be sent the API key.
        using var linkResponse = await SendAsync(
            apiKey!, HttpMethod.Post, "download",
            JsonContent.Create(new { file_id = ParseFileId(providerFileId) }),
            cancellationToken);

        if (linkResponse == null) return null;

        var link = await ReadDownloadResponseAsync(linkResponse, cancellationToken);

        // The quota answer is not only a status code: a 200 can still carry
        // remaining=0 with the refusal in `message`, so both are checked.
        var quota = DescribeQuotaFailure(linkResponse.StatusCode, link?.Remaining, link?.Message, link?.Link);
        if (quota != null) throw new SubtitleProviderException(quota, isQuotaExhausted: true);

        if (!linkResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenSubtitles refused a download link for {FileId} ({Status}).", providerFileId, linkResponse.StatusCode);
            return null;
        }

        if (link?.Link == null) return null;

        if (link.Remaining is int remaining and <= LowQuotaWarningThreshold)
        {
            _logger.LogWarning("OpenSubtitles downloads remaining today: {Remaining} (resets {Reset}).", remaining, link.ResetTime ?? "unknown");
        }

        try
        {
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

        settings.TryGetValue("auth_mode", out var mode);
        settings.TryGetValue("username", out var username);
        settings.TryGetValue("password", out var password);
        var plan = OpenSubtitlesAuthPlan.Resolve(mode, username, password);

        try
        {
            if (plan.UsesAccount)
            {
                var session = await LoginAsync(apiKey.Trim(), plan, cancellationToken);
                return session == null
                    ? PluginConnectionTestResult.Fail("OpenSubtitles rejected the username or password.")
                    : PluginConnectionTestResult.Ok($"Signed in as {plan.Username}. Downloads will use your account quota.");
            }

            using var client = CreateClient(apiKey.Trim(), DefaultBaseUrl, bearerToken: null);
            using var response = await client.GetAsync("infos/languages", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return plan.Warning == null
                    ? PluginConnectionTestResult.Ok("OpenSubtitles accepted the API key.")
                    : PluginConnectionTestResult.Ok($"OpenSubtitles accepted the API key. {plan.Warning}");
            }
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

    // Sends with whatever credentials the settings call for, and re-logs in once
    // if the token has expired underneath us. A token outlives most sessions but
    // not all of them, and an expiry that forced the admin to re-save settings
    // would look like the feature breaking at random.
    private async Task<HttpResponseMessage?> SendAsync(string apiKey, HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        var plan = await ResolveAuthPlanAsync();
        if (plan.Warning != null) _logger.LogWarning("OpenSubtitles: {Warning}", plan.Warning);

        var session = plan.UsesAccount ? await EnsureSessionAsync(apiKey, plan, cancellationToken) : null;
        var response = await SendOnceAsync(apiKey, session, method, url, content, cancellationToken);

        if (response != null && response.StatusCode == HttpStatusCode.Unauthorized && session != null)
        {
            response.Dispose();
            _logger.LogInformation("OpenSubtitles rejected the cached session token; signing in again.");
            InvalidateSession();

            var renewed = await EnsureSessionAsync(apiKey, plan, cancellationToken);
            if (renewed == null) return null;

            response = await SendOnceAsync(apiKey, renewed, method, url, content, cancellationToken);
        }

        if (response != null && !await EnsureNotRateLimitedAsync(response)) return null;
        return response;
    }

    private async Task<HttpResponseMessage?> SendOnceAsync(string apiKey, OpenSubtitlesSession? session, HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var client = CreateClient(apiKey, session?.BaseUrl ?? DefaultBaseUrl, session?.Token);
        using var request = new HttpRequestMessage(method, url);

        // The same content instance cannot be sent twice, so a retry after a
        // re-login re-serializes it rather than reusing a consumed stream.
        if (content != null) request.Content = await CloneContentAsync(content, cancellationToken);

        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<OpenSubtitlesSession?> EnsureSessionAsync(string apiKey, OpenSubtitlesAuthPlan plan, CancellationToken cancellationToken)
    {
        var current = _session;
        if (current != null && current.Username == plan.Username) return current;

        await LoginGate.WaitAsync(cancellationToken);
        try
        {
            current = _session;
            if (current != null && current.Username == plan.Username) return current;

            return await LoginAsync(apiKey, plan, cancellationToken);
        }
        finally
        {
            LoginGate.Release();
        }
    }

    private async Task<OpenSubtitlesSession?> LoginAsync(string apiKey, OpenSubtitlesAuthPlan plan, CancellationToken cancellationToken)
    {
        if (!plan.UsesAccount) return null;

        try
        {
            using var client = CreateClient(apiKey, DefaultBaseUrl, bearerToken: null);
            using var response = await client.PostAsJsonAsync("login", new { username = plan.Username, password = plan.Password }, cancellationToken);

            if (!await EnsureNotRateLimitedAsync(response)) return null;

            if (!response.IsSuccessStatusCode)
            {
                // Deliberately logs the status and nothing else: the body of a
                // failed login echoes the credentials back.
                _logger.LogWarning("OpenSubtitles sign-in failed for the configured account ({Status}).", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(Json, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload?.Token)) return null;

            var session = new OpenSubtitlesSession(payload.Token!, NormalizeBaseUrl(payload.BaseUrl), plan.Username!);
            _session = session;

            _logger.LogInformation("Signed in to OpenSubtitles; using {BaseUrl} for this session.", session.BaseUrl);
            return session;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSubtitles sign-in threw.");
            return null;
        }
    }

    // Login answers with the host this account should use — a VIP account gets a
    // different one — and the docs are explicit that later calls go there rather
    // than to the default.
    public static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return DefaultBaseUrl;

        var host = baseUrl.Trim();
        if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase)) host = "https://" + host;
        if (!host.EndsWith('/')) host += "/";
        if (!host.Contains("/api/v1", StringComparison.OrdinalIgnoreCase)) host += "api/v1/";

        return host;
    }

    public const int LowQuotaWarningThreshold = 5;

    // OpenSubtitles reports an exhausted download allowance in more than one
    // way: 406 historically, 429 when it is a rate limit, and a 200 whose body
    // says remaining=0 with no link. All three have to read as "out of
    // downloads" or the viewer sees a generic failure and simply tries again.
    public static string? DescribeQuotaFailure(HttpStatusCode status, int? remaining, string? message, string? link)
    {
        var apiMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        if (status == HttpStatusCode.NotAcceptable)
        {
            return apiMessage ?? "OpenSubtitles download limit reached. The quota resets daily.";
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            return apiMessage ?? "OpenSubtitles is rate-limiting this server. Try again in a few minutes.";
        }

        if (remaining is <= 0 && string.IsNullOrWhiteSpace(link))
        {
            return apiMessage ?? "OpenSubtitles download limit reached. The quota resets daily.";
        }

        return null;
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

    // Drops the cached sign-in and any rate-limit hold. Credentials changing
    // under a live session is exactly the case a cached token gets wrong.
    public static void ResetTransientState()
    {
        _session = null;
        _blockedUntilUtc = null;
    }

    private static void InvalidateSession() => _session = null;

    private static async Task<HttpContent> CloneContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        var clone = new ByteArrayContent(bytes);
        if (content.Headers.ContentType != null) clone.Headers.ContentType = content.Headers.ContentType;
        return clone;
    }

    private static async Task<DownloadResponse?> ReadDownloadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<DownloadResponse>(Json, cancellationToken);
        }
        catch (Exception)
        {
            // A gateway or CDN error page is not JSON; the status alone then has
            // to carry the meaning.
            return null;
        }
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

    private HttpClient CreateClient(string apiKey, string baseUrl, string? bearerToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Remove(ApiKeyHeader);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

        // The API key identifies the consumer in BOTH modes; the bearer only
        // adds which account the download is billed to.
        client.DefaultRequestHeaders.Authorization = bearerToken == null
            ? null
            : new AuthenticationHeaderValue("Bearer", bearerToken);

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

    private async Task<OpenSubtitlesAuthPlan> ResolveAuthPlanAsync() =>
        OpenSubtitlesAuthPlan.Resolve(
            await GetSettingAsync("auth_mode"),
            await GetSettingAsync("username"),
            await GetSettingAsync("password"));

    private async Task<string?> GetSettingAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        return await settings.GetSettingAsync(PluginId, key);
    }

    private class LoginResponse
    {
        public string? Token { get; set; }

        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; set; }

        public int? Status { get; set; }
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

        public int? Requests { get; set; }
        public int? Remaining { get; set; }
        public string? Message { get; set; }

        [JsonPropertyName("reset_time")]
        public string? ResetTime { get; set; }
    }
}
