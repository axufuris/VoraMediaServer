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

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("VoraMediaServer/1.0");
        request.Headers.Accept.ParseAdd("image/*");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Remote server returned {(int)response.StatusCode}.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
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

        return memory.ToArray();
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
