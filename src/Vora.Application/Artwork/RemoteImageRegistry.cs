using System.Collections.Concurrent;

namespace Vora.Application.Artwork;

[AttributeUsage(AttributeTargets.Property)]
public sealed class RemoteImageAttribute : Attribute;

public interface IRemoteImageRegistry
{
    void Remember(string? url);
    bool IsKnown(string? url);
}

public sealed class RemoteImageRegistry : IRemoteImageRegistry
{
    public const int MaxEntries = 250_000;

    private const string InsecurePrefix = "http://";

    private readonly ConcurrentDictionary<string, byte> _urls = new(StringComparer.Ordinal);

    public void Remember(string? url)
    {
        if (!IsInsecureImageUrl(url)) return;

        if (_urls.Count >= MaxEntries) _urls.Clear();
        _urls.TryAdd(url, 0);
    }

    public bool IsKnown(string? url) => IsInsecureImageUrl(url) && _urls.ContainsKey(url);

    private static bool IsInsecureImageUrl([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? url) =>
        url != null && url.StartsWith(InsecurePrefix, StringComparison.OrdinalIgnoreCase);
}
