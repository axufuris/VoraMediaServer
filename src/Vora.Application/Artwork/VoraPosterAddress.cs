using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Vora.Application.Artwork;

public static partial class VoraPosterAddress
{
    public const string Prefix = "/api/artwork/vora/";
    private const string Extension = ".jpg";

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex Segment();

    public static bool Is([NotNullWhen(true)] string? address) =>
        address != null && address.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static string Build(string category, string id, string version) =>
        $"{Prefix}{category}/{id}{Extension}?v={version}";

    public static bool TryParse([NotNullWhen(true)] string? address, [NotNullWhen(true)] out string? category, [NotNullWhen(true)] out string? id)
    {
        category = null;
        id = null;
        if (!Is(address)) return false;

        var path = address[Prefix.Length..];
        var query = path.IndexOf('?');
        if (query >= 0) path = path[..query];

        var slash = path.IndexOf('/');
        return slash > 0 && TryReadFile(path[..slash], path[(slash + 1)..], out category, out id);
    }

    public static bool TryReadFile(string category, string fileName, [NotNullWhen(true)] out string? posterCategory, [NotNullWhen(true)] out string? id)
    {
        posterCategory = null;
        id = null;
        if (!fileName.EndsWith(Extension, StringComparison.Ordinal)) return false;

        var name = fileName[..^Extension.Length];
        if (!Segment().IsMatch(category) || !Segment().IsMatch(name)) return false;

        posterCategory = category;
        id = name;
        return true;
    }
}
