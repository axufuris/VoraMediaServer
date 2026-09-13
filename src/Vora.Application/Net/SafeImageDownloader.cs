using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Net;

public interface ISafeImageDownloader
{
    Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default);
}

public class SafeImageDownloader : ISafeImageDownloader
{
    public const string HttpClientName = "Vora.SafeImageDownloader";

    private const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxRedirects = 5;
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/avif",
        "image/bmp"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SafeImageDownloader> _logger;

    public SafeImageDownloader(IHttpClientFactory httpClientFactory, ILogger<SafeImageDownloader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("URL is required.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("URL is not a valid absolute URI.");
        }

        if (!AllowedSchemes.Contains(uri.Scheme))
        {
            throw new InvalidOperationException($"Only http/https URLs are allowed. Scheme '{uri.Scheme}' is rejected.");
        }

        await EnsureHostIsPublicAsync(uri.Host, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(FetchTimeout);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var response = await SendFollowingRedirectsAsync(client, uri, cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote server returned {(int)response.StatusCode}.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var contentTypeIsTrusted = !string.IsNullOrWhiteSpace(contentType) && AllowedContentTypes.Contains(contentType);

        // A header that does not name an image is not proof it is not one: plenty
        // of hosts answer application/octet-stream, and some send nothing at all.
        // The bytes are checked below either way, which is a stronger test than
        // believing the header, so an unhelpful one is not on its own a refusal.
        if (!contentTypeIsTrusted && !string.IsNullOrWhiteSpace(contentType) && !IsAmbiguousContentType(contentType))
        {
            throw new InvalidOperationException($"Unsupported content type '{contentType}'. Expected an image.");
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > MaxBytes)
        {
            throw new InvalidOperationException($"Image exceeds maximum size of {MaxBytes} bytes (reported {contentLength.Value}).");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var memory = new MemoryStream();

        var buffer = new byte[8192];
        var total = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token)) > 0)
        {
            total += read;
            if (total > MaxBytes)
            {
                throw new InvalidOperationException($"Image exceeds maximum size of {MaxBytes} bytes during download.");
            }
            await memory.WriteAsync(buffer.AsMemory(0, read), cts.Token);
        }

        var bytes = memory.ToArray();

        if (!LooksLikeImage(bytes))
        {
            throw new InvalidOperationException(contentTypeIsTrusted
                ? "The download did not contain a readable image."
                : $"The URL returned '{contentType ?? "no content type"}' and the content is not an image.");
        }

        return bytes;
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        var current = uri;

        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("VoraMediaServer/1.0");
            request.Headers.Accept.ParseAdd("image/*");

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location == null) return response;

            if (hop >= MaxRedirects)
            {
                response.Dispose();
                throw new InvalidOperationException($"URL redirected more than {MaxRedirects} times.");
            }

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);
            response.Dispose();

            if (!AllowedSchemes.Contains(next.Scheme))
            {
                throw new InvalidOperationException($"Redirect to scheme '{next.Scheme}' is not allowed.");
            }

            // The point of following by hand: every hop is re-checked, so a public
            // URL cannot bounce the request onto an internal address.
            await EnsureHostIsPublicAsync(next.Host, cancellationToken);
            current = next;
        }
    }

    public static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    public static bool IsAmbiguousContentType(string contentType) =>
        contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("binary/octet-stream", StringComparison.OrdinalIgnoreCase);

    // Checked against the bytes rather than the header, so a host that mislabels
    // an image still works and one that labels a non-image as a JPEG still does not.
    public static bool LooksLikeImage(byte[] bytes)
    {
        if (bytes.Length < 12) return false;

        // JPEG, PNG, GIF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8) return true;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true;
        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;

        var riff = bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46;
        var webp = bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        if (riff && webp) return true;

        // AVIF and other ISO-BMFF images carry 'ftyp' at offset 4.
        return bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70;
    }

    private async Task EnsureHostIsPublicAsync(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("URL host is required.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var direct))
        {
            addresses = new[] { direct };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS resolution failed for image host {Host}.", host);
                throw new InvalidOperationException($"Could not resolve host '{host}'.");
            }
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"Host '{host}' did not resolve to any addresses.");
        }

        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
            {
                throw new InvalidOperationException($"Host '{host}' resolves to a non-public address and is not allowed.");
            }
        }
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            if (bytes[0] == 10) return true;
            if (bytes[0] == 127) return true;
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
            if (bytes[0] >= 224) return true;
            if (bytes[0] == 0) return true;
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal) return true;
            if (address.IsIPv6SiteLocal) return true;
            if (address.IsIPv6Multicast) return true;

            var bytes = address.GetAddressBytes();
            if (bytes[0] == 0xfc || bytes[0] == 0xfd) return true;
            if (address.Equals(IPAddress.IPv6Loopback)) return true;
        }

        return false;
    }
}
