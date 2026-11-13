using System.Collections.Concurrent;

namespace Vora.Application.Artwork;

[AttributeUsage(AttributeTargets.Property)]
public sealed class RemoteImageAttribute : Attribute;

public interface IRemoteImageRegistry
{
    void Remember(string? url);
    bool IsKnown(string? url);
}

// Remembers the third-party image addresses the server itself has sent to a
// client ([RemoteImage] fields: station and channel logos, podcast art), so the
// image cache will fetch and re-encode those and nothing else. Both http and
// https are kept: an http logo is proxied so an https page can show it, and any
// logo is proxied for a client that can't decode its format (a Roku can't read
// an .ico). The downloader still refuses private and loopback hosts.
public sealed class RemoteImageRegistry : IRemoteImageRegistry
{
    public const int MaxEntries = 250_000;

    private readonly ConcurrentDictionary<string, byte> _urls = new(StringComparer.Ordinal);

    public void Remember(string? url)
    {
        if (!IsWebImageUrl(url)) return;

        if (_urls.Count >= MaxEntries) _urls.Clear();
        _urls.TryAdd(url, 0);
    }

    public bool IsKnown(string? url) => IsWebImageUrl(url) && _urls.ContainsKey(url);

    private static bool IsWebImageUrl([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        url != null && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
