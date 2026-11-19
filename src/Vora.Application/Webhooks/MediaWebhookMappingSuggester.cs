namespace Vora.Application.Webhooks;

public sealed record MediaWebhookMappingSuggestion(string Mappings, IReadOnlyList<string> Unmatched);

public static class MediaWebhookMappingSuggester
{
    public static MediaWebhookMappingSuggestion Suggest(IEnumerable<string> rootFolders, IReadOnlyList<string> libraryFolders)
    {
        var candidates = Candidates(libraryFolders);
        var lines = new List<string>();
        var unmatched = new List<string>();

        foreach (var root in rootFolders.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var match = BestMatch(root, candidates) ?? (libraryFolders.Count == 1 ? libraryFolders[0] : null);

            if (match == null)
            {
                unmatched.Add(root);
                continue;
            }

            lines.Add($"{root.TrimEnd('/', '\\')} => {match}");
        }

        return new MediaWebhookMappingSuggestion(string.Join('\n', lines), unmatched);
    }

    private static List<string> Candidates(IReadOnlyList<string> libraryFolders)
    {
        var candidates = libraryFolders.ToList();

        foreach (var folder in libraryFolders)
        {
            var trimmed = folder.TrimEnd('/', '\\');
            for (var cut = trimmed.LastIndexOfAny(new[] { '/', '\\' }); cut > 0; cut = trimmed.LastIndexOfAny(new[] { '/', '\\' }))
            {
                trimmed = trimmed[..cut];
                if (!candidates.Contains(trimmed, StringComparer.Ordinal)) candidates.Add(trimmed);
            }
        }

        return candidates;
    }

    private static string? BestMatch(string root, List<string> candidates)
    {
        var wanted = MediaWebhookPathResolver.Segments(root);
        string? best = null;
        var bestScore = 0;

        foreach (var candidate in candidates)
        {
            var score = SharedTail(wanted, MediaWebhookPathResolver.Segments(candidate));
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private static int SharedTail(string[] left, string[] right)
    {
        var score = 0;
        while (score < left.Length && score < right.Length
            && string.Equals(left[^(score + 1)], right[^(score + 1)], StringComparison.OrdinalIgnoreCase))
        {
            score++;
        }

        return score;
    }
}
