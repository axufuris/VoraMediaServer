using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.LastFm;

public class LastFmListeningDataProvider : IListeningDataProvider, IPluginConnectionTest
{
    public const string HttpClientName = "LastFmHttpClient";
    private const string BaseUrl = "https://ws.audioscrobbler.com/2.0/";
    private const string AuthUrlTemplate = "https://www.last.fm/api/auth/?api_key={0}&token={1}";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LastFmListeningDataProvider> _logger;

    public string Id => "lastfm_listening";
    public string Name => "Last.fm Scrobbling";
    public string ProviderName => "Last.fm";
    public string Version => "1.0.0";
    public string Description => "Scrobbles plays to Last.fm and updates Now Playing. Each user authenticates from their profile settings. Requires an API key + secret from https://www.last.fm/api/account/create.";
    public bool IsSystemPlugin => true;
    public string Type => "ListeningData";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Music };

    public LastFmListeningDataProvider(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, ILogger<LastFmListeningDataProvider> logger)
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
                Key = "api_key",
                Label = "Last.fm API Key",
                Type = "password",
                DefaultValue = string.Empty,
                Required = true,
                Placeholder = "Paste your Last.fm API key",
                Description = "Last.fm API Key (free). Request one at https://www.last.fm/api/account/create — fill in any Application Name, Description, and Homepage URL (Last.fm doesn't validate them). The page immediately returns both an API Key and a Shared Secret; copy the API Key here and paste the Shared Secret into the field below. Scrobbling requires both."
            },
            new PluginSettingDefinitionDto
            {
                Key = "api_secret",
                Label = "Last.fm API Secret",
                Type = "password",
                DefaultValue = string.Empty,
                Required = true,
                Placeholder = "Paste your Last.fm shared secret",
                Description = "Last.fm Shared Secret. Returned alongside your API Key when you registered at https://www.last.fm/api/account/create. Required for signed write operations (scrobbling, Now Playing updates) — without it, reads still work but plays cannot be submitted."
            }
        };
    }

    public async Task<PluginConnectionTestResult> TestConnectionAsync(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        if (!settings.TryGetValue("api_key", out var apiKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            return PluginConnectionTestResult.Fail("Enter an API key first.");
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.GetAsync($"http://ws.audioscrobbler.com/2.0/?method=auth.getToken&api_key={Uri.EscapeDataString(apiKey.Trim())}&format=json", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode && !body.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
        {
            return PluginConnectionTestResult.Ok("Last.fm accepted the API key.");
        }
        return PluginConnectionTestResult.Fail("Last.fm rejected the API key.");
    }

    private async Task<(string? Key, string? Secret)> GetCredentialsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IPluginSettingsProvider>();
        var key = await settings.GetSettingAsync(Id, "api_key");
        var secret = await settings.GetSettingAsync(Id, "api_secret");
        return (string.IsNullOrWhiteSpace(key) ? null : key, string.IsNullOrWhiteSpace(secret) ? null : secret);
    }

    public async Task<string?> GetAuthTokenAsync(CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = await GetCredentialsAsync();
        if (apiKey == null || apiSecret == null) return null;

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["method"] = "auth.getToken"
        };
        SignAndFormat(parameters, apiSecret);

        var url = BaseUrl + "?" + ToQuery(parameters) + "&format=json";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Last.fm auth.getToken failed");
            return null;
        }
    }

    public async Task<string> BuildAuthUrlAsync(string token, CancellationToken cancellationToken)
    {
        var (apiKey, _) = await GetCredentialsAsync();
        if (apiKey == null) return string.Empty;
        return string.Format(AuthUrlTemplate, Uri.EscapeDataString(apiKey), Uri.EscapeDataString(token));
    }

    public async Task<ListeningSession?> ExchangeTokenForSessionAsync(string token, CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = await GetCredentialsAsync();
        if (apiKey == null || apiSecret == null) return null;

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["method"] = "auth.getSession",
            ["token"] = token
        };
        SignAndFormat(parameters, apiSecret);

        var url = BaseUrl + "?" + ToQuery(parameters) + "&format=json";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("session", out var session)) return null;
            var sk = session.TryGetProperty("key", out var skEl) ? skEl.GetString() : null;
            var name = session.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (string.IsNullOrEmpty(sk) || string.IsNullOrEmpty(name)) return null;
            return new ListeningSession { SessionKey = sk, Username = name };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Last.fm auth.getSession failed");
            return null;
        }
    }

    public async Task<bool> ScrobbleAsync(string sessionKey, string artist, string track, string? album, DateTime playedAt, int? durationSeconds, CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = await GetCredentialsAsync();
        if (apiKey == null || apiSecret == null) return false;
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track)) return false;

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["artist"] = artist,
            ["method"] = "track.scrobble",
            ["sk"] = sessionKey,
            ["timestamp"] = new DateTimeOffset(playedAt.ToUniversalTime()).ToUnixTimeSeconds().ToString(),
            ["track"] = track
        };
        if (!string.IsNullOrWhiteSpace(album)) parameters["album"] = album;
        if (durationSeconds.HasValue && durationSeconds.Value > 0) parameters["duration"] = durationSeconds.Value.ToString();
        SignAndFormat(parameters, apiSecret);

        return await PostAsync(parameters, cancellationToken);
    }

    public async Task<bool> UpdateNowPlayingAsync(string sessionKey, string artist, string track, string? album, int? durationSeconds, CancellationToken cancellationToken)
    {
        var (apiKey, apiSecret) = await GetCredentialsAsync();
        if (apiKey == null || apiSecret == null) return false;
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track)) return false;

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["api_key"] = apiKey,
            ["artist"] = artist,
            ["method"] = "track.updateNowPlaying",
            ["sk"] = sessionKey,
            ["track"] = track
        };
        if (!string.IsNullOrWhiteSpace(album)) parameters["album"] = album;
        if (durationSeconds.HasValue && durationSeconds.Value > 0) parameters["duration"] = durationSeconds.Value.ToString();
        SignAndFormat(parameters, apiSecret);

        return await PostAsync(parameters, cancellationToken);
    }

    private async Task<bool> PostAsync(SortedDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var body = new FormUrlEncodedContent(parameters.Concat(new[] { new KeyValuePair<string, string>("format", "json") }));
            using var response = await client.PostAsync(BaseUrl, body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Last.fm POST {Method} failed: {Status} {Body}", parameters["method"], response.StatusCode, err);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Last.fm POST {Method} threw", parameters.TryGetValue("method", out var m) ? m : "?");
            return false;
        }
    }

    private static void SignAndFormat(SortedDictionary<string, string> parameters, string apiSecret)
    {
        var sb = new StringBuilder();
        foreach (var kv in parameters) sb.Append(kv.Key).Append(kv.Value);
        sb.Append(apiSecret);
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append(b.ToString("x2"));
        parameters["api_sig"] = hex.ToString();
    }

    private static string ToQuery(IEnumerable<KeyValuePair<string, string>> parameters)
    {
        return string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task<IReadOnlyList<SimilarArtistResult>> GetSimilarArtistsAsync(string artistName, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return Array.Empty<SimilarArtistResult>();
        var (apiKey, _) = await GetCredentialsAsync();
        if (apiKey == null) return Array.Empty<SimilarArtistResult>();

        var url = $"{BaseUrl}?method=artist.getSimilar&artist={Uri.EscapeDataString(artistName)}&api_key={Uri.EscapeDataString(apiKey)}&limit={limit}&format=json";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<SimilarArtistResult>();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("similarartists", out var sa)) return Array.Empty<SimilarArtistResult>();
            if (!sa.TryGetProperty("artist", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<SimilarArtistResult>();

            var results = new List<SimilarArtistResult>();
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var match = 0d;
                if (item.TryGetProperty("match", out var m))
                {
                    if (m.ValueKind == JsonValueKind.String && double.TryParse(m.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        match = parsed;
                    else if (m.ValueKind == JsonValueKind.Number)
                        match = m.GetDouble();
                }
                results.Add(new SimilarArtistResult { Name = name, Score = match });
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Last.fm getSimilar failed for {Artist}", artistName);
            return Array.Empty<SimilarArtistResult>();
        }
    }

    public async Task<IReadOnlyList<ArtistTagResult>> GetArtistTopTagsAsync(string artistName, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artistName)) return Array.Empty<ArtistTagResult>();
        var (apiKey, _) = await GetCredentialsAsync();
        if (apiKey == null) return Array.Empty<ArtistTagResult>();

        var url = $"{BaseUrl}?method=artist.getTopTags&artist={Uri.EscapeDataString(artistName)}&api_key={Uri.EscapeDataString(apiKey)}&format=json";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<ArtistTagResult>();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("toptags", out var tt)) return Array.Empty<ArtistTagResult>();
            if (!tt.TryGetProperty("tag", out var arr) || arr.ValueKind != JsonValueKind.Array) return Array.Empty<ArtistTagResult>();

            var results = new List<ArtistTagResult>();
            foreach (var item in arr.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var weight = 0;
                if (item.TryGetProperty("count", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Number) weight = c.GetInt32();
                    else if (c.ValueKind == JsonValueKind.String && int.TryParse(c.GetString(), out var parsed)) weight = parsed;
                }
                results.Add(new ArtistTagResult { Tag = name, Weight = weight });
                if (results.Count >= limit) break;
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Last.fm getTopTags failed for {Artist}", artistName);
            return Array.Empty<ArtistTagResult>();
        }
    }
}
