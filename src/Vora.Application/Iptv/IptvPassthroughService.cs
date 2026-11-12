using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Users;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

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

public sealed class PassthroughAudioStream : IDisposable
{
    private readonly Action _keepAlive;
    private readonly Action _release;
    private bool _disposed;

    public PassthroughAudioStream(HttpResponseMessage response, Action keepAlive, Action release)
    {
        Response = response;
        _keepAlive = keepAlive;
        _release = release;
    }

    public HttpResponseMessage Response { get; }

    public void KeepAlive() => _keepAlive();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Response.Dispose();
        _release();
    }
}

public interface IIptvPassthroughService
{
    Task<PassthroughStartResult> StartPassthroughAsync(Guid channelId, Guid userId, Guid? profileId);
    Task<PassthroughPlaylistResult?> GetRewrittenPlaylistAsync(string token);
    Task<HttpResponseMessage?> FetchSegmentAsync(string token, CancellationToken cancellationToken);
    Task<PassthroughAudioStream?> FetchAudioStreamAsync(string token, CancellationToken cancellationToken);
}

public class IptvPassthroughService : IIptvPassthroughService
{
    public const string DefaultAudioUserAgent = "WinampMPEG/5.0";

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
    private readonly IIptvUpstreamResolver _resolver;
    private readonly IIptvPassthroughTokenProtector _tokens;
    private readonly IIptvManager _iptvManager;
    private readonly ILogger<IptvPassthroughService> _logger;

    public IptvPassthroughService(
        IIptvRepository repository,
        IUserManager userManager,
        IHttpClientFactory httpClientFactory,
        ITunerRegistry tunerRegistry,
        IIptvUpstreamResolver resolver,
        IIptvPassthroughTokenProtector tokens,
        IIptvManager iptvManager,
        ILogger<IptvPassthroughService> logger)
    {
        _repository = repository;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _tunerRegistry = tunerRegistry;
        _resolver = resolver;
        _tokens = tokens;
        _iptvManager = iptvManager;
        _logger = logger;
    }

    public static string LiveLeaseKey(string leaseId) => $"live:{leaseId}";

    private static string ViewerLeasePrefix(Guid viewerKey, Guid playlistId) => $"{viewerKey:N}.{playlistId:N}.";

    public static string ViewerLiveLeasePrefix(Guid viewerKey, Guid playlistId) => LiveLeaseKey(ViewerLeasePrefix(viewerKey, playlistId));

    public static bool NeedsRemux(IptvChannelKind channelKind, string streamType) =>
        channelKind == IptvChannelKind.Tv && streamType != StreamTypeHls;

    private sealed record PassthroughTicket(string Tag, Guid PlaylistId, Guid ChannelId, string LeaseId, string Url);

    private sealed record UpstreamContext(IptvRequestPolicy Policy, int ConnectionLimit, string? Secret);

    public async Task<PassthroughStartResult> StartPassthroughAsync(Guid channelId, Guid userId, Guid? profileId)
    {
        var channel = await _repository.GetChannelWithPlaylistAsync(channelId)
            ?? throw new InvalidOperationException("Channel not found.");

        if (!await IptvPlaylistAccess.AllowsAsync(_userManager, userId, profileId, channel.PlaylistId))
        {
            throw new UnauthorizedAccessException("User does not have access to this IPTV playlist.");
        }

        IptvPlaylist? playlist = channel.Playlist;
        var upstream = _resolver.Resolve(playlist, channel);
        var limit = playlist is null ? 0 : IptvConnectionLimit.Of(playlist);

        var (resolvedUrl, streamType) = await ResolveStreamAsync(upstream.Url, upstream.Policy, channel.Name, depth: 0);

        var viewerKey = profileId ?? Guid.NewGuid();
        var viewerLeases = ViewerLeasePrefix(viewerKey, channel.PlaylistId);
        _tunerRegistry.ReleaseStartingWith(ViewerLiveLeasePrefix(viewerKey, channel.PlaylistId));

        if (NeedsRemux(channel.Kind, streamType))
        {
            var remuxUrl = await _iptvManager.StartLiveRemuxAsync(channel, upstream with { Url = resolvedUrl }, viewerKey, limit);
            return new PassthroughStartResult
            {
                Url = remuxUrl,
                StreamType = StreamTypeHls
            };
        }

        if (channel.Kind == IptvChannelKind.Tv)
        {
            await _iptvManager.StopLiveRemuxAsync(viewerKey);
        }

        var leaseId = viewerLeases + Guid.NewGuid().ToString("N");
        if (streamType == StreamTypeAudio)
        {
            var audioLease = limit > 0 ? leaseId : string.Empty;
            if (audioLease.Length > 0 && !_tunerRegistry.TryAcquire(channel.PlaylistId, limit, LiveLeaseKey(audioLease), TunerLeaseKind.Live))
            {
                throw new TunerLimitReachedException();
            }

            var audioToken = Sign(new PassthroughTicket(AudioTokenTag, channel.PlaylistId, channel.Id, audioLease, resolvedUrl), AudioTokenTtlMinutes);
            return new PassthroughStartResult
            {
                Url = $"{PassthroughBasePath}/audio?t={audioToken}",
                StreamType = StreamTypeAudio
            };
        }

        if (!_tunerRegistry.TryAcquire(channel.PlaylistId, limit, LiveLeaseKey(leaseId), TunerLeaseKind.Live))
        {
            throw new TunerLimitReachedException();
        }

        var playlistToken = Sign(new PassthroughTicket(PlaylistTokenTag, channel.PlaylistId, channel.Id, leaseId, resolvedUrl), PlaylistTokenTtlMinutes);
        return new PassthroughStartResult
        {
            Url = $"{PassthroughBasePath}/playlist.m3u8?t={playlistToken}",
            StreamType = StreamTypeHls
        };
    }

    private async Task<(string Url, string Type)> ResolveStreamAsync(string upstreamUrl, IptvRequestPolicy policy, string channelName, int depth, bool assumeAudioIfUnknown = false)
    {
        if (depth > 3)
        {
            _logger.LogWarning("Passthrough indirection depth exceeded for {Channel}; defaulting to audio.", channelName);
            return (upstreamUrl, StreamTypeAudio);
        }

        var lowerPath = upstreamUrl.ToLowerInvariant();
        var queryIdx = lowerPath.IndexOf('?');
        if (queryIdx >= 0) lowerPath = lowerPath[..queryIdx];

        if (lowerPath.EndsWith(".m3u8")) return (upstreamUrl, StreamTypeHls);
        if (lowerPath.EndsWith(".ts") || lowerPath.EndsWith(".mpegts") || lowerPath.EndsWith(".mp4") || lowerPath.EndsWith(".mkv"))
        {
            return (upstreamUrl, StreamTypeAudio);
        }
        if (lowerPath.EndsWith(".mp3") || lowerPath.EndsWith(".aac")
            || lowerPath.EndsWith(".ogg") || lowerPath.EndsWith(".opus")
            || lowerPath.EndsWith(".flac") || lowerPath.EndsWith(".wav"))
        {
            return (upstreamUrl, StreamTypeAudio);
        }

        if (lowerPath.EndsWith(".pls"))
        {
            var inner = await FollowIndirectionAsync(upstreamUrl, policy, channelName, ExtractFirstFileFromPls);
            if (inner != null) return await ResolveStreamAsync(inner, policy, channelName, depth + 1, assumeAudioIfUnknown: true);
            return (upstreamUrl, StreamTypeAudio);
        }

        if (lowerPath.EndsWith(".m3u"))
        {
            var body = await TryFetchTextAsync(upstreamUrl, policy, channelName);
            if (body != null && body.TrimStart().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                return (upstreamUrl, StreamTypeHls);
            }
            var inner = body == null ? null : ExtractFirstUrlFromShoutcastM3u(body);
            if (inner != null) return await ResolveStreamAsync(inner, policy, channelName, depth + 1, assumeAudioIfUnknown: true);
            return (upstreamUrl, StreamTypeAudio);
        }

        if (assumeAudioIfUnknown)
        {
            return (upstreamUrl, StreamTypeAudio);
        }

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        try
        {
            using var request = policy.Get(upstreamUrl);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;

            if (contentType.Contains("mpegurl") || contentType.Contains("m3u8")) return (upstreamUrl, StreamTypeHls);
            if (contentType.Contains("scpls") || contentType.Contains("pls"))
            {
                var inner = await FollowIndirectionAsync(upstreamUrl, policy, channelName, ExtractFirstFileFromPls);
                if (inner != null) return await ResolveStreamAsync(inner, policy, channelName, depth + 1, assumeAudioIfUnknown: true);
            }
            if (contentType.StartsWith("audio/")) return (upstreamUrl, StreamTypeAudio);

            return (upstreamUrl, StreamTypeAudio);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Passthrough probe failed for {Channel}; defaulting to audio: {ErrorType}", channelName, ex.GetType().Name);
            return (upstreamUrl, StreamTypeAudio);
        }
    }

    private async Task<string?> FollowIndirectionAsync(string url, IptvRequestPolicy policy, string channelName, Func<string, string?> extractor)
    {
        var body = await TryFetchTextAsync(url, policy, channelName);
        return body == null ? null : extractor(body);
    }

    private async Task<string?> TryFetchTextAsync(string url, IptvRequestPolicy policy, string channelName)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
            using var request = policy.Get(url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Passthrough indirection fetch failed for {Channel}: {ErrorType}", channelName, ex.GetType().Name);
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

    public async Task<PassthroughAudioStream?> FetchAudioStreamAsync(string token, CancellationToken cancellationToken)
    {
        if (!TryReadTicket(token, AudioTokenTag, out var ticket)) return null;

        var context = await UpstreamContextAsync(ticket);
        if (context is null) return null;

        if (!IsHttpUrl(ticket.Url))
        {
            _logger.LogWarning("Passthrough audio token resolved to a non-HTTP address {Url}; refusing.", Redact(ticket.Url, context.Secret));
            return null;
        }

        var leaseKey = ticket.LeaseId.Length > 0 && context.ConnectionLimit > 0 ? LiveLeaseKey(ticket.LeaseId) : null;
        if (leaseKey is not null && !_tunerRegistry.TryAcquire(ticket.PlaylistId, context.ConnectionLimit, leaseKey, TunerLeaseKind.Live))
        {
            throw new TunerLimitReachedException();
        }

        try
        {
            var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
            var request = context.Policy.Get(ticket.Url, DefaultAudioUserAgent);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new PassthroughAudioStream(
                response,
                () => { if (leaseKey is not null) _tunerRegistry.Heartbeat(leaseKey); },
                () => { if (leaseKey is not null) _tunerRegistry.Release(leaseKey); });
        }
        catch
        {
            if (leaseKey is not null) _tunerRegistry.Release(leaseKey);
            throw;
        }
    }

    public async Task<PassthroughPlaylistResult?> GetRewrittenPlaylistAsync(string token)
    {
        if (!TryReadTicket(token, PlaylistTokenTag, out var ticket))
        {
            _logger.LogWarning("Passthrough playlist token verification failed.");
            return null;
        }

        if (ticket.LeaseId.Length > 0)
        {
            _tunerRegistry.Heartbeat(LiveLeaseKey(ticket.LeaseId));
        }

        var context = await UpstreamContextAsync(ticket);
        if (context is null) return null;

        var upstreamUrl = ticket.Url;
        _logger.LogDebug("Passthrough fetching upstream playlist: {Url}", Redact(upstreamUrl, context.Secret));

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);

        HttpResponseMessage response;
        try
        {
            using var request = context.Policy.Get(upstreamUrl);
            response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Passthrough upstream fetch threw for {Url}: {Error}", Redact(upstreamUrl, context.Secret), Redact(ex.Message, context.Secret));
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var snippet = await SafeReadSnippetAsync(response);
                _logger.LogWarning("Passthrough upstream returned {Status} for {Url}. Body snippet: {Body}", response.StatusCode, Redact(upstreamUrl, context.Secret), Redact(snippet, context.Secret));
                return null;
            }

            var upstreamContentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var rawContent = await response.Content.ReadAsStringAsync();
            var effectiveUrl = response.RequestMessage?.RequestUri?.ToString() ?? upstreamUrl;

            if (!IsHlsPlaylistContent(upstreamContentType, rawContent))
            {
                var snippet = rawContent.Length > 200 ? rawContent[..200] : rawContent;
                _logger.LogWarning("Passthrough upstream {Url} returned 200 but content was not HLS. Content-Type: {Type}. Body snippet: {Body}", Redact(upstreamUrl, context.Secret), upstreamContentType, Redact(snippet, context.Secret));
                return null;
            }

            if (!string.Equals(effectiveUrl, upstreamUrl, StringComparison.Ordinal))
            {
                _logger.LogDebug("Passthrough master URL was redirected. Requested: {Requested}, Final: {Final}", Redact(upstreamUrl, context.Secret), Redact(effectiveUrl, context.Secret));
            }

            _logger.LogDebug("Passthrough upstream returned valid HLS ({Length} bytes, type {Type}).", rawContent.Length, upstreamContentType);

            var rewritten = RewritePlaylist(rawContent, effectiveUrl, ticket, context.Secret);
            return new PassthroughPlaylistResult
            {
                Content = rewritten,
                ContentType = "application/vnd.apple.mpegurl"
            };
        }
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
        if (!TryReadTicket(token, SegmentTokenTag, out var ticket)) return null;

        var context = await UpstreamContextAsync(ticket);
        if (context is null) return null;

        if (!IsHttpUrl(ticket.Url))
        {
            _logger.LogWarning("Passthrough segment token resolved to a non-HTTP address {Url}; refusing.", Redact(ticket.Url, context.Secret));
            return null;
        }

        var client = _httpClientFactory.CreateClient(IptvManager.HttpClientName);
        using var request = context.Policy.Get(ticket.Url);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private async Task<UpstreamContext?> UpstreamContextAsync(PassthroughTicket ticket)
    {
        var channel = await _repository.GetChannelWithPlaylistAsync(ticket.ChannelId);
        if (channel is null || channel.PlaylistId != ticket.PlaylistId) return null;

        IptvPlaylist? playlist = channel.Playlist;
        try
        {
            return new UpstreamContext(
                _resolver.PolicyFor(playlist, channel),
                playlist is null ? 0 : IptvConnectionLimit.Of(playlist),
                SecretOf(playlist));
        }
        catch (IptvSourceException ex)
        {
            _logger.LogWarning("Passthrough for {Channel} can't reach its provider: {Error}", channel.Name, ex.Message);
            return null;
        }
    }

    private string? SecretOf(IptvPlaylist? playlist)
    {
        if (playlist is null) return null;
        try
        {
            return _resolver.PasswordFor(playlist);
        }
        catch (IptvSourceException)
        {
            return null;
        }
    }

    private static string Redact(string text, string? secret) => IptvUrlRedactor.Redact(text, secret);

    private string RewritePlaylist(string content, string upstreamUrl, PassthroughTicket ticket, string? secret)
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
                sb.Append(RewriteTagAttributes(line, baseUri, ticket, secret));
                sb.Append('\n');
                continue;
            }

            var absolute = ResolveUri(line, baseUri);
            if (!IsHttpUrl(absolute))
            {
                _logger.LogWarning("Passthrough playlist contained a non-HTTP address ({Resolved}); leaving the line unchanged.", Redact(absolute, secret));
                sb.Append(line);
                sb.Append('\n');
                continue;
            }

            sb.Append(RewriteUriLine(absolute, ticket));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private string RewriteUriLine(string absoluteUrl, PassthroughTicket ticket)
    {
        if (LooksLikePlaylist(absoluteUrl))
        {
            var token = Sign(ticket with { Tag = PlaylistTokenTag, Url = absoluteUrl }, PlaylistTokenTtlMinutes);
            return $"playlist.m3u8?t={token}";
        }

        var segmentToken = Sign(ticket with { Tag = SegmentTokenTag, LeaseId = string.Empty, Url = absoluteUrl }, SegmentTokenTtlMinutes);
        return $"segment?t={segmentToken}";
    }

    private string RewriteTagAttributes(string line, Uri baseUri, PassthroughTicket ticket, string? secret)
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
            _logger.LogDebug("Passthrough tag had a non-HTTP address {Uri}; preserving the original.", Redact(absolute, secret));
            return line;
        }

        var rewritten = RewriteUriLine(absolute, ticket);
        return string.Concat(line.AsSpan(0, start), rewritten, line.AsSpan(end));
    }

    internal static string ResolveUri(string reference, Uri baseUri)
    {
        if (reference.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(reference, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(reference, UriKind.Relative, out var relative) && Uri.TryCreate(baseUri, relative, out var resolved)
            ? resolved.ToString()
            : reference;
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

    private string Sign(PassthroughTicket ticket, int ttlMinutes)
    {
        var payload = string.Join('|', ticket.Tag, ticket.PlaylistId.ToString("N"), ticket.ChannelId.ToString("N"), ticket.LeaseId, ticket.Url);
        return _tokens.Protect(payload, DateTimeOffset.UtcNow.AddMinutes(ttlMinutes));
    }

    private bool TryReadTicket(string token, string expectedTag, out PassthroughTicket ticket)
    {
        ticket = new PassthroughTicket(string.Empty, Guid.Empty, Guid.Empty, string.Empty, string.Empty);
        if (string.IsNullOrEmpty(token) || !_tokens.TryUnprotect(token, out var payload)) return false;

        var parts = payload.Split('|', 5);
        if (parts.Length != 5) return false;
        if (!string.Equals(parts[0], expectedTag, StringComparison.Ordinal)) return false;
        if (!Guid.TryParseExact(parts[1], "N", out var playlistId) || !Guid.TryParseExact(parts[2], "N", out var channelId)) return false;

        ticket = new PassthroughTicket(parts[0], playlistId, channelId, parts[3], parts[4]);
        return true;
    }
}
