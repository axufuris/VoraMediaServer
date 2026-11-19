namespace Vora.Application.Webhooks;

public sealed record MediaWebhookPathMapping(string From, string To);

public static class MediaWebhookPathMappings
{
    private const string Separator = "=>";

    public static IReadOnlyList<MediaWebhookPathMapping> Parse(string? text)
    {
        var mappings = new List<MediaWebhookPathMapping>();
        if (string.IsNullOrWhiteSpace(text)) return mappings;

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = line.IndexOf(Separator, StringComparison.Ordinal);
            if (at < 0) continue;

            var from = line[..at].Trim();
            var to = line[(at + Separator.Length)..].Trim();
            if (from.Length == 0 || to.Length == 0) continue;

            mappings.Add(new MediaWebhookPathMapping(from, to));
        }

        return mappings;
    }

    public static string? FirstBadLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = line.IndexOf(Separator, StringComparison.Ordinal);
            if (at <= 0 || line[..at].Trim().Length == 0 || line[(at + Separator.Length)..].Trim().Length == 0)
            {
                return line;
            }
        }

        return null;
    }
}

public sealed class MediaWebhookPathResolver
{
    private static readonly char[] Separators = { '/', '\\' };

    private readonly IReadOnlyList<MediaWebhookPathMapping> _mappings;
    private readonly IReadOnlyList<MediaWebhookLibrary> _libraries;
    private readonly Func<string, Task<bool>> _anchorExists;
    private readonly StringComparison _comparison;

    public MediaWebhookPathResolver(
        IReadOnlyList<MediaWebhookPathMapping> mappings,
        IReadOnlyList<MediaWebhookLibrary> libraries,
        Func<string, Task<bool>> anchorExists)
    {
        _mappings = mappings;
        _libraries = libraries;
        _anchorExists = anchorExists;
        _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public static string[] Segments(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    public async Task<MediaWebhookResolution> ResolveAsync(string reportedPath)
    {
        if (string.IsNullOrWhiteSpace(reportedPath))
        {
            return MediaWebhookResolution.NotFound("The path is empty.");
        }

        var segments = Segments(reportedPath);
        if (segments.Length == 0 || segments.Contains(".."))
        {
            return MediaWebhookResolution.NotFound("The path is empty or climbs out of its folder.");
        }

        foreach (var mapping in _mappings.OrderByDescending(m => Segments(m.From).Length))
        {
            var from = Segments(mapping.From);
            if (from.Length == 0 || !StartsWith(segments, from, StringComparison.OrdinalIgnoreCase)) continue;

            var mapped = Combine(mapping.To, segments.Skip(from.Length));
            return Inside(mapped) is { } target
                ? MediaWebhookResolution.Found(target)
                : MediaWebhookResolution.NotFound($"The mapping {mapping.From} => {mapping.To} puts this file at {mapped}, which is not inside any library folder of this kind.");
        }

        if (Inside(reportedPath) is { } direct)
        {
            return MediaWebhookResolution.Found(direct);
        }

        for (var skip = 1; skip < segments.Length; skip++)
        {
            foreach (var root in Roots())
            {
                var top = Combine(root.Root, new[] { segments[skip] });
                if (!await _anchorExists(top)) continue;

                var candidate = Combine(root.Root, segments.Skip(skip));
                return MediaWebhookResolution.Found(new MediaWebhookTarget(root.Library, root.Root, candidate));
            }
        }

        return MediaWebhookResolution.NotFound("No library folder of this kind matches this path. Add a path mapping for it.");
    }

    private IEnumerable<(MediaWebhookLibrary Library, string Root)> Roots() =>
        _libraries.SelectMany(l => l.FolderPaths.Select(f => (Library: l, Root: f)));

    private MediaWebhookTarget? Inside(string path)
    {
        var segments = Segments(path);
        MediaWebhookTarget? best = null;
        var bestLength = -1;

        foreach (var (library, root) in Roots())
        {
            var rootSegments = Segments(root);
            if (rootSegments.Length == 0 || rootSegments.Length <= bestLength) continue;
            if (!StartsWith(segments, rootSegments, _comparison)) continue;

            best = new MediaWebhookTarget(library, root, Combine(root, segments.Skip(rootSegments.Length)));
            bestLength = rootSegments.Length;
        }

        return best;
    }

    private static bool StartsWith(string[] segments, string[] prefix, StringComparison comparison)
    {
        if (prefix.Length > segments.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(segments[i], prefix[i], comparison)) return false;
        }
        return true;
    }

    private static string Combine(string root, IEnumerable<string> rest)
    {
        var path = root;
        foreach (var segment in rest)
        {
            path = Path.Combine(path, segment);
        }
        return path;
    }
}
