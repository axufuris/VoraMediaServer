namespace Vora.Application.Webhooks;

public sealed record MediaWebhookMappingSuggestion(string Mappings, IReadOnlyList<string> Unmatched);

public static class MediaWebhookMappingSuggester
{
    public static MediaWebhookMappingSuggestion Suggest(IEnumerable<string> rootFolders, IReadOnlyList<string> libraryFolders)
    {
        var lines = new List<string>();
        var unmatched = new List<string>();

        foreach (var root in rootFolders.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var leaf = Leaf(root);
            var match = libraryFolders.FirstOrDefault(f => string.Equals(Leaf(f), leaf, StringComparison.OrdinalIgnoreCase))
                ?? (libraryFolders.Count == 1 ? libraryFolders[0] : null);

            if (match == null)
            {
                unmatched.Add(root);
                continue;
            }

            lines.Add($"{root.TrimEnd('/', '\\')} => {match}");
        }

        return new MediaWebhookMappingSuggestion(string.Join('\n', lines), unmatched);
    }

    private static string Leaf(string path) => MediaWebhookPathResolver.Segments(path).LastOrDefault() ?? string.Empty;
}
