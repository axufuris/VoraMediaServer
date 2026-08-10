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

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return new List<CollectionSyncItemDto>();
        }

        var prompt =
            $"You are building a media collection described as: \"{externalId}\".\n" +
            "List every movie and TV season that belongs in it. Return ONLY valid JSON of the form " +
            "{\"items\": [ {\"type\": \"movie\", \"title\": \"...\", \"year\": 2008}, " +
            "{\"type\": \"season\", \"show\": \"...\", \"season\": 1} ]}.\n" +
            "Rules: use \"movie\" for films (include the release year) and \"season\" for a single season of a TV show " +
            "(give the show's title and the season number as an integer). For every TV show that belongs, output a SEPARATE " +
            "entry for EACH season it has — season 1, season 2, season 3, and so on. Do NOT collapse a multi-season show into " +
            "one entry and do NOT stop at season 1 (e.g. a show with three seasons yields three entries). Be exhaustive and " +
            "include everything that belongs, even lesser-known titles. Do not include individual episodes. Do not invent " +
            "titles that do not exist. Use the widely-recognized English title for each show and movie.";

        var json = await openAi.CompleteJsonAsync(Id, prompt, temperature: 0.2, modelSettingKey: "collections_model");
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<CollectionSyncItemDto>();
        }

        ListResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ListResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new List<CollectionSyncItemDto>();
        }

        if (parsed?.Items == null)
        {
            return new List<CollectionSyncItemDto>();
        }

        var results = new List<CollectionSyncItemDto>();
        foreach (var item in parsed.Items)
        {
            if (string.Equals(item.Type, "season", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(item.Show) || item.Season == null) continue;
                results.Add(new CollectionSyncItemDto
                {
                    MediaType = "Season",
                    ShowTitle = item.Show,
                    SeasonNumber = item.Season
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(item.Title)) continue;
                results.Add(new CollectionSyncItemDto
                {
                    MediaType = "Movie",
                    Title = item.Title,
                    Year = item.Year
                });
            }
        }

        return results;
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
