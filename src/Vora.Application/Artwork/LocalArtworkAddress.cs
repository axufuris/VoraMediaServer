using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Vora.Application.Artwork;

public static class LocalArtworkAddress
{
    public const string Prefix = "/api/artwork/local/";

    public static bool Is([NotNullWhen(true)] string? address) =>
        address != null && address.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Build(Guid mediaItemId, string filePath)
    {
        var version = File.GetLastWriteTimeUtc(filePath).Ticks.ToString(CultureInfo.InvariantCulture);
        return $"{Prefix}{mediaItemId:D}/{Uri.EscapeDataString(Path.GetFileName(filePath))}?v={version}";
    }

    public static string? Rehome(string? address, Guid from, Guid to)
    {
        if (!TryParse(address, out var owner, out _) || owner != from) return address;

        var rest = address[Prefix.Length..];
        return $"{Prefix}{to:D}{rest[rest.IndexOf('/')..]}";
    }

    public static bool TryParse([NotNullWhen(true)] string? address, out Guid mediaItemId, [NotNullWhen(true)] out string? fileName)
    {
        mediaItemId = Guid.Empty;
        fileName = null;
        if (!Is(address)) return false;

        var path = address[Prefix.Length..];
        var query = path.IndexOf('?');
        if (query >= 0) path = path[..query];

        var slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return false;
        if (!Guid.TryParse(path[..slash], out mediaItemId)) return false;

        fileName = Uri.UnescapeDataString(path[(slash + 1)..]);
        return fileName.Length > 0;
    }
}
