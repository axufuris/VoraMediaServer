using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Users;

namespace Vora.Application.Iptv;

public class PassthroughPlaylistResult
{
    public required string Content { get; init; }
    public required string ContentType { get; init; }
}

public class PassthroughStartResult
{
    public required string Url { get; init; }
    public required string StreamType { get; init; }
}

public interface IIptvPassthroughService
{
    Task<PassthroughStartResult> StartPassthroughAsync(Guid channelId, Guid userId);
    Task<PassthroughPlaylistResult?> GetRewrittenPlaylistAsync(string token);
    Task<HttpResponseMessage?> FetchSegmentAsync(string token, CancellationToken cancellationToken);
    Task<HttpResponseMessage?> FetchAudioStreamAsync(string token, CancellationToken cancellationToken);
}

public class IptvPassthroughService : IIptvPassthroughService
{
    private const string PlaylistTokenTag = "p";
    private const string SegmentTokenTag = "s";
    private const string AudioTokenTag = "a";
    private const int PlaylistTokenTtlMinutes = 240;
    private const int SegmentTokenTtlMinutes = 30;
    private const int AudioTokenTtlMinutes = 240;
    private const string PassthroughBasePath = "/api/iptv/passthrough";

    public const string StreamTypeHls = "hls";
    public const string StreamTypeAudio = "audio";

    private readonly IIptvRepository _repository;
    private readonly IUserManager _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITunerRegistry _tunerRegistry;
    private readonly IptvPassthroughOptions _passthroughOptions;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<IptvPassthroughService> _logger;

    public IptvPassthroughService(
        IIptvRepository repository,
        IUserManager userManager,
        IHttpClientFactory httpClientFactory,
        ITunerRegistry tunerRegistry,
        IOptions<IptvPassthroughOptions> passthroughOptions,
        IOptions<JwtOptions> jwtOptions,
        ILogger<IptvPassthroughService> logger)
    {
        _repository = repository;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _tunerRegistry = tunerRegistry;
        _passthroughOptions = passthroughOptions.Value;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    private static string LiveLeaseKey(string leaseId) => $"live:{leaseId}";

    public async Task<PassthroughStartResult> StartPassthroughAsync(Guid channelId, Guid userId)
    {
        var channel = await _repository.GetChannelByIdAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found.");

        await EnsureUserHasChannelAccessAsync(userId, channel.PlaylistId);

        var (resolvedUrl, streamType) = await ResolveStreamAsync(channel.StreamUrl, depth: 0);

        if (streamType == StreamTypeAudio)
        {
            var audioToken = SignToken(AudioTokenTag, resolvedUrl, DateTime.UtcNow.AddMinutes(AudioTokenTtlMinutes));
            return new PassthroughStartResult
            {
                Url = $"{PassthroughBasePath}/audio?t={audioToken}",
                StreamType = StreamTypeAudio
            };
        }

        var tunerProfile = await _repository.GetTunerProfileByPlaylistIdAsync(channel.PlaylistId);
        var maxConcurrent = tunerProfile?.MaxConcurrentStreams ?? 0;
        var leaseId = Guid.NewGuid().ToString("N");
        if (!_tunerRegistry.TryAcquire(channel.PlaylistId, maxConcurrent, LiveLeaseKey(leaseId), TunerLeaseKind.Live))
        {
            throw new TunerLimitReachedException();
        }

        var playlistToken = SignToken(PlaylistTokenTag, $"{leaseId}|{resolvedUrl}", DateTime.UtcNow.AddMinutes(PlaylistTokenTtlMinutes));
        return new PassthroughStartResult
        {
            Url = $"{PassthroughBasePath}/playlist.m3u8?t={playlistToken}",
            StreamType = StreamTypeHls
        };
    }

    private async Task<(string Url, string Type)> ResolveStreamAsync(string upstreamUrl, int depth, bool assumeAudioIfUnknown = false)
    {
        if (depth > 3)
        {
            _logger.LogWarning("Passthrough indirection depth exceeded for {Url}; defaulting to audio.", upstreamUrl);
            return (upstreamUrl, StreamTypeAudio);
        }

        var lowerPath = upstreamUrl.ToLowerInvariant();
        var queryIdx = lowerPath.IndexOf('?');
        if (queryIdx >= 0) lowerPath = lowerPath[..queryIdx];

        if (lowerPath.EndsWith(".m3u8")) return (upstreamUrl, StreamTypeHls);
        if (lowerPath.EndsWith(".mp3") || lowerPath.EndsWith(".aac")
            || lowerPath.EndsWith(".ogg") || lowerPath.EndsWith(".opus")
            || lowerPath.EndsWith(".flac") || lowerPath.EndsWith(".wav"))
        {
            return (upstreamUrl, StreamTypeAudio);
        }

        if (lowerPath.EndsWith(".pls"))
        {
            var inner = await FollowIndirectionAsync(upstreamUrl, ExtractFirstFileFromPls);
            if (inner != null) return await ResolveStreamAsync(inner, depth + 1, assumeAudioIfUnknown: true);
            return (upstreamUrl, StreamTypeAudio);
        }

        if (lowerPath.EndsWith(".m3u"))
        {
            var body = await TryFetchTextAsync(upstreamUrl);
            if (body != null && body.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                return (upstreamUrl, StreamTypeHls);
            }
            var inner = body == null ? null : ExtractFirstUrlFromShoutcastM3u(body);
            if (inner != null) return await ResolveStreamAsync(inner, depth + 1, assumeAudioIfUnknown: true);
            return (upstreamUrl, StreamTypeAudio);
        }

        if (assumeAudioIfUnknown)
        {
            return (upstreamUrl, StreamTypeAudio);
        }

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        try
        {
            using var response = await client.GetAsync(upstreamUrl, HttpCompletionOption.ResponseHeadersRead);
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

            if (contentType.Contains("mpegurl") || contentType.Contains("m3u8")) return (upstreamUrl, StreamTypeHls);
            if (contentType.Contains("scpls") || contentType.Contains("pls"))
            {
                var inner = await FollowIndirectionAsync(upstreamUrl, ExtractFirstFileFromPls);
                if (inner != null) return await ResolveStreamAsync(inner, depth + 1, assumeAudioIfUnknown: true);
            }
            if (contentType.StartsWith("audio/")) return (upstreamUrl, StreamTypeAudio);

            return (upstreamUrl, StreamTypeAudio);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Passthrough probe failed for {Url}; defaulting to audio.", upstreamUrl);
            return (upstreamUrl, StreamTypeAudio);
        }
    }

    private async Task<string?> FollowIndirectionAsync(string url, Func<string, string?> extractor)
    {
        var body = await TryFetchTextAsync(url);
        return body == null ? null : extractor(body);
    }

    private async Task<string?> TryFetchTextAsync(string url)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
            return await client.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Passthrough indirection fetch failed for {Url}.", url);
            return null;
        }
    }

    private static string? ExtractFirstFileFromPls(string plsContent)
    {
        foreach (var rawLine in plsContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                var eq = line.IndexOf('=');
                if (eq >= 0 && eq < line.Length - 1)
                {
                    return line[(eq + 1)..].Trim();
                }
            }
        }
        return null;
    }

    private static string? ExtractFirstUrlFromShoutcastM3u(string m3uContent)
    {
        foreach (var rawLine in m3uContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;
            return line;
        }
        return null;
    }

    public async Task<HttpResponseMessage?> FetchAudioStreamAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryVerifyToken(token, AudioTokenTag, out var upstreamUrl)) return null;

        if (!IsHttpUrl(upstreamUrl))
        {
            _logger.LogWarning("Passthrough audio token resolved to non-HTTP URL {Url}; refusing.", upstreamUrl);
            return null;
        }

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        var request = new HttpRequestMessage(HttpMethod.Get, upstreamUrl);
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", "WinampMPEG/5.0");
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public async Task<PassthroughPlaylistResult?> GetRewrittenPlaylistAsync(string token)
    {
        if (!TryVerifyToken(token, PlaylistTokenTag, out var payload))
        {
            _logger.LogWarning("Passthrough playlist token verification failed.");
            return null;
        }

        var (leaseId, upstreamUrl) = SplitPlaylistPayload(payload);
        if (leaseId.Length > 0)
        {
            _tunerRegistry.Heartbeat(LiveLeaseKey(leaseId));
        }

        _logger.LogDebug("Passthrough fetching upstream playlist: {Url}", upstreamUrl);

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(upstreamUrl, HttpCompletionOption.ResponseContentRead);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Passthrough upstream fetch threw for {Url}.", upstreamUrl);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var snippet = await SafeReadSnippetAsync(response);
            _logger.LogWarning("Passthrough upstream returned {Status} for {Url}. Body snippet: {Body}", response.StatusCode, upstreamUrl, snippet);
            return null;
        }

        var upstreamContentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var rawContent = await response.Content.ReadAsStringAsync();
        var effectiveUrl = response.RequestMessage?.RequestUri?.ToString() ?? upstreamUrl;

        if (!IsHlsPlaylistContent(upstreamContentType, rawContent))
        {
            var snippet = rawContent.Length > 200 ? rawContent[..200] : rawContent;
            _logger.LogWarning("Passthrough upstream {Url} returned 200 but content was not HLS. Content-Type: {Type}. Body snippet: {Body}", upstreamUrl, upstreamContentType, snippet);
            return null;
        }

        if (!string.Equals(effectiveUrl, upstreamUrl, StringComparison.Ordinal))
        {
            _logger.LogDebug("Passthrough master URL was redirected. Requested: {Requested}, Final: {Final}", upstreamUrl, effectiveUrl);
        }

        _logger.LogDebug("Passthrough upstream returned valid HLS ({Length} bytes, type {Type}).", rawContent.Length, upstreamContentType);

        var rewritten = RewritePlaylist(rawContent, effectiveUrl, leaseId);
        return new PassthroughPlaylistResult
        {
            Content = rewritten,
            ContentType = "application/vnd.apple.mpegurl"
        };
    }

    private static (string LeaseId, string Url) SplitPlaylistPayload(string payload)
    {
        var sep = payload.IndexOf('|');
        if (sep < 0)
        {
            return (string.Empty, payload);
        }
        return (payload[..sep], payload[(sep + 1)..]);
    }

    private static async Task<string> SafeReadSnippetAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            return body.Length > 300 ? body[..300] : body;
        }
        catch
        {
            return "(unreadable)";
        }
    }

    public async Task<HttpResponseMessage?> FetchSegmentAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryVerifyToken(token, SegmentTokenTag, out var upstreamUrl)) return null;

        if (!IsHttpUrl(upstreamUrl))
        {
            _logger.LogWarning("Passthrough segment token resolved to non-HTTP URL {Url}; refusing.", upstreamUrl);
            return null;
        }

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        return await client.GetAsync(upstreamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task EnsureUserHasChannelAccessAsync(Guid userId, Guid playlistId)
    {
        var user = await _userManager.GetUserAccountAsync(userId)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.HasAllIptvAccess) return;
        if (user.AllowedIptvPlaylistIds.Contains(playlistId)) return;
        throw new UnauthorizedAccessException("User does not have access to this IPTV playlist.");
    }

    private string RewritePlaylist(string content, string upstreamUrl, string leaseId)
    {
        var baseUri = new Uri(upstreamUrl);
        var sb = new StringBuilder(content.Length + 256);

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            if (line.StartsWith('#'))
            {
                sb.Append(RewriteTagAttributes(line, baseUri, leaseId));
                sb.Append('\n');
                continue;
            }

            var absolute = ResolveUri(line, baseUri);
            if (!IsHttpUrl(absolute))
            {
                _logger.LogWarning("Passthrough playlist contained non-HTTP URL (original {Original}, resolved {Resolved}); leaving line unchanged.", line, absolute);
                sb.Append(line);
                sb.Append('\n');
                continue;
            }

            sb.Append(RewriteUriLine(absolute, leaseId));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private string RewriteUriLine(string absoluteUrl, string leaseId)
    {
        if (LooksLikePlaylist(absoluteUrl))
        {
            var token = SignToken(PlaylistTokenTag, $"{leaseId}|{absoluteUrl}", DateTime.UtcNow.AddMinutes(PlaylistTokenTtlMinutes));
            return $"playlist.m3u8?t={token}";
        }

        var segmentToken = SignToken(SegmentTokenTag, absoluteUrl, DateTime.UtcNow.AddMinutes(SegmentTokenTtlMinutes));
        return $"segment?t={segmentToken}";
    }

    private string RewriteTagAttributes(string line, Uri baseUri, string leaseId)
    {
        const string marker = "URI=\"";
        var idx = line.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return line;

        var start = idx + marker.Length;
        var end = line.IndexOf('"', start);
        if (end < 0) return line;

        var originalUri = line.Substring(start, end - start);
        var absolute = ResolveUri(originalUri, baseUri);
        if (!IsHttpUrl(absolute))
        {
            _logger.LogDebug("Passthrough tag had non-HTTP URI {Uri}; preserving original.", absolute);
            return line;
        }

        var rewritten = RewriteUriLine(absolute, leaseId);
        return string.Concat(line.AsSpan(0, start), rewritten, line.AsSpan(end));
    }

    private static string ResolveUri(string maybeRelative, Uri baseUri)
    {
        if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }
        return new Uri(baseUri, maybeRelative).ToString();
    }

    private static bool IsHttpUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikePlaylist(string url)
    {
        var path = url;
        var queryIdx = url.IndexOf('?');
        if (queryIdx >= 0) path = url[..queryIdx];
        return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHlsPlaylistContent(string contentType, string content)
    {
        var lower = contentType.ToLowerInvariant();
        if (lower.Contains("mpegurl") || lower.Contains("m3u8") || lower.Contains("m3u")) return true;
        return content.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase);
    }

    private string SignToken(string tag, string payload, DateTime expiresAt)
    {
        var exp = ((DateTimeOffset)expiresAt).ToUnixTimeSeconds();
        var data = $"{tag}|{exp}|{payload}";
        var key = Encoding.UTF8.GetBytes(GetSecret());
        using var hmac = new HMACSHA256(key);
        var sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(data))}.{Base64UrlEncode(sig)}";
    }

    private bool TryVerifyToken(string token, string expectedTag, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrEmpty(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        try
        {
            var dataBytes = Base64UrlDecode(parts[0]);
            var providedSig = Base64UrlDecode(parts[1]);
            var key = Encoding.UTF8.GetBytes(GetSecret());
            using var hmac = new HMACSHA256(key);
            var expectedSig = hmac.ComputeHash(dataBytes);

            if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig)) return false;

            var data = Encoding.UTF8.GetString(dataBytes);
            var firstSep = data.IndexOf('|');
            if (firstSep < 0) return false;
            var secondSep = data.IndexOf('|', firstSep + 1);
            if (secondSep < 0) return false;

            var tag = data[..firstSep];
            if (!string.Equals(tag, expectedTag, StringComparison.Ordinal)) return false;

            var expString = data.Substring(firstSep + 1, secondSep - firstSep - 1);
            if (!long.TryParse(expString, out var exp)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;

            payload = data[(secondSep + 1)..];
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Passthrough token verification failed.");
            return false;
        }
    }

    private string GetSecret()
    {
        if (!string.IsNullOrWhiteSpace(_passthroughOptions.SecretKey))
        {
            return _passthroughOptions.SecretKey;
        }
        return _jwtOptions.SecretKey ?? string.Empty;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
