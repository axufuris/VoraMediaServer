using System.Text;
using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Ai;

public class OpenAiListProvider(IOpenAiClient openAi) : ICollectionSyncProvider
{
    public string Id => "openai_list";
    public string Name => "AI List";
    public string Version => "1.0.0";
    public string Description => "Fills a collection from an AI-generated list. Instead of a list URL, describe the movies and shows you want and Vora matches them to your library.";
    public bool IsSystemPlugin => true;
    public bool IsAiPlugin => true;
    public string Type => "Collection_Sync";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ExternalIdLabel => "Describe the list";
    public string ExternalIdPlaceholder => "e.g., Every Marvel Cinematic Universe movie and Disney+ series";

    private const int MaxCompletenessPasses = 2;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return new List<CollectionSyncItemDto>();
        }

        var results = new List<CollectionSyncItemDto>();
        var seen = new HashSet<string>();

        var initial = ParseItems(await openAi.CompleteJsonAsync(Id, BuildListPrompt(externalId), temperature: 0.2, modelSettingKey: "collections_model"));
        AddNew(initial, results, seen);
        if (results.Count == 0)
        {
            return results;
        }

        for (var pass = 0; pass < MaxCompletenessPasses; pass++)
        {
            var additions = ParseItems(await openAi.CompleteJsonAsync(Id, BuildCompletenessPrompt(externalId, results), temperature: 0.2, modelSettingKey: "collections_model"));
            if (AddNew(additions, results, seen) == 0)
            {
                break;
            }
        }

        return results;
    }

    private static string BuildListPrompt(string externalId) =>
        $"You are building a media collection described as: \"{externalId}\".\n" +
        "List every movie and TV season that belongs in it. Treat short films, one-shots, TV specials, and " +
        "featurettes that are officially part of it as movies and include them too. Return ONLY valid JSON of the form " +
        "{\"items\": [ {\"type\": \"movie\", \"title\": \"...\", \"year\": 2008}, " +
        "{\"type\": \"season\", \"show\": \"...\", \"season\": 1} ]}.\n" +
        "Rules: use \"movie\" for films (include the release year) and \"season\" for a single season of a TV show " +
        "(give the show's title and the season number as an integer). For every TV show that belongs, output a SEPARATE " +
        "entry for EACH season it has — season 1, season 2, season 3, and so on. Do NOT collapse a multi-season show into " +
        "one entry and do NOT stop at season 1 (e.g. a show with three seasons yields three entries). Be exhaustive and " +
        "include everything that belongs, even lesser-known titles. Do not include individual episodes. Do not invent " +
        "titles that do not exist. Use the widely-recognized English title for each show and movie.";

    private static string BuildCompletenessPrompt(string externalId, List<CollectionSyncItemDto> current)
    {
        var lines = new StringBuilder();
        foreach (var item in current)
        {
            lines.Append(string.Equals(item.MediaType, "Season", StringComparison.OrdinalIgnoreCase)
                ? $"- {item.ShowTitle} Season {item.SeasonNumber} [season]\n"
                : $"- {item.Title}{(item.Year.HasValue ? $" ({item.Year})" : string.Empty)} [movie]\n");
        }

        return
            $"You are auditing a media collection described as: \"{externalId}\".\n" +
            "This is the list produced so far — do NOT repeat any of these:\n" +
            lines +
            "\nName every movie and TV season that BELONGS in the collection but is MISSING from the list above — including " +
            "later seasons of a show that only has some seasons listed, short films, one-shots, and specials. Apply the same " +
            "rules: one entry per season, treat shorts/specials as movies, never invent a title that does not exist. Return " +
            "ONLY the missing entries as valid JSON of the form {\"items\": [ {\"type\": \"movie\", \"title\": \"...\", " +
            "\"year\": 2008}, {\"type\": \"season\", \"show\": \"...\", \"season\": 1} ]}. Return {\"items\": []} if nothing is missing.";
    }

    private static List<ListItem> ParseItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ListItem>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<ListResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return parsed?.Items ?? new List<ListItem>();
        }
        catch (JsonException)
        {
            return new List<ListItem>();
        }
    }

    private static int AddNew(List<ListItem> items, List<CollectionSyncItemDto> results, HashSet<string> seen)
    {
        var added = 0;
        foreach (var item in items)
        {
            CollectionSyncItemDto dto;
            string key;

            if (string.Equals(item.Type, "season", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(item.Show) || item.Season == null) continue;
                key = $"s|{Normalize(item.Show)}|{item.Season}";
                dto = new CollectionSyncItemDto { MediaType = "Season", ShowTitle = item.Show, SeasonNumber = item.Season };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(item.Title)) continue;
                key = $"m|{Normalize(item.Title)}";
                dto = new CollectionSyncItemDto { MediaType = "Movie", Title = item.Title, Year = item.Year };
            }

            if (seen.Add(key))
            {
                results.Add(dto);
                added++;
            }
        }

        return added;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private class ListResult
    {
        public List<ListItem>? Items { get; set; }
    }

    private class ListItem
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public int? Year { get; set; }
        public string? Show { get; set; }
        public int? Season { get; set; }
    }
}
