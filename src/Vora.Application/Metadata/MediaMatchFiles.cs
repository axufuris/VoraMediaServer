using System.Text.RegularExpressions;

namespace Vora.Application.Metadata;

public static partial class MediaMatchFiles
{
    public static string FileName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public static string? ShowFolderName(string episodePath, IEnumerable<string> libraryRoots)
    {
        var path = episodePath.Replace('\\', '/');

        var roots = libraryRoots
            .Select(r => r.Replace('\\', '/').TrimEnd('/'))
            .Where(r => r.Length > 0)
            .OrderByDescending(r => r.Length);
        foreach (var root in roots)
        {
            if (!path.StartsWith(root + "/", StringComparison.Ordinal)) continue;

            var rest = path[(root.Length + 1)..];
            var slash = rest.IndexOf('/');
            return slash > 0 ? rest[..slash] : null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = segments.Length - 2; i >= 0; i--)
        {
            if (!SeasonFolder().IsMatch(segments[i])) return segments[i];
        }

        return null;
    }

    [GeneratedRegex(@"^(season|series|specials?)\b|^s\d{1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeasonFolder();
}
