using System.Text.Json;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Ai;

public class OpenAiChronologyProvider(IOpenAiClient openAi) : IChronologyProvider
{
    public string Id => "openai_chronology";
    public string Name => "AI Chronological Order";
    public string Version => "1.0.0";
    public string Description => "Orders a collection chronologically using AI. Instead of a list URL, describe the collection and how it should be ordered.";
    public bool IsSystemPlugin => true;
    public bool IsAiPlugin => true;
    public string Type => "Chronology";
    public string DeveloperName => "Andy Xufuris";
    public IEnumerable<LibraryKind> SupportedLibraryKinds => new[] { LibraryKind.Movie, LibraryKind.TvShow };

    public string ProviderId => "openai";
    public string ProviderName => "AI Chronological Order";

    public string ExternalIdLabel => "Describe the collection and ordering";
    public string ExternalIdPlaceholder => "e.g., DC Extended Universe films & shows in in-universe chronological order";

    public bool OrdersLocalItemsOnly => true;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return new List<ChronologyResult>();
        }

        var description = string.IsNullOrWhiteSpace(externalId) ? collectionName : externalId;
        var lines = items.Select(i => $"{i.Index}: {DescribeItem(i)}");

        var prompt =
            $"You are ordering a media collection. The collection is described as: \"{description}\".\n" +
            "Here are the items, one per line as `index: Title (Year) [Type]`. The year shown is the RELEASE year.\n" +
            string.Join("\n", lines) + "\n\n" +
            "Sort them into the collection's intended chronological order. Unless the description explicitly asks for release " +
            "order, order by the IN-UNIVERSE story timeline (when the events take place within the story), which often differs " +
            "from the release year — a prequel, flashback, or origin story is placed earlier than a later-set film released before " +
            "it. Use the well-known franchise timeline where one exists.\n" +
            "Return ONLY valid JSON of the form {\"order\": [<index>, <index>, ...]}. You MUST include EVERY index shown above " +
            "exactly once — never omit, invent, or duplicate an index.";

        var json = await openAi.CompleteJsonAsync(Id, prompt, cancellationToken, temperature: 0.2);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ChronologyResult>();
        }

        int[]? order;
        try
        {
            order = JsonSerializer.Deserialize<OrderResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Order;
        }
        catch (JsonException)
        {
            return new List<ChronologyResult>();
        }

        if (order == null || order.Length == 0)
        {
            return new List<ChronologyResult>();
        }

        var byIndex = items.ToDictionary(i => i.Index);
        var results = new List<ChronologyResult>();
        var placed = new HashSet<int>();
        decimal position = 1;

        foreach (var index in order)
        {
            if (!placed.Add(index) || !byIndex.TryGetValue(index, out var item)) continue;
            results.Add(ToResult(item, position++));
        }

        // Anything the model dropped keeps its original relative order at the end.
        foreach (var item in items)
        {
            if (placed.Add(item.Index)) results.Add(ToResult(item, position++));
        }

        return results;
    }

    private static string DescribeItem(CollectionOrderingItemDto item)
    {
        if (string.Equals(item.MediaType, "Season", StringComparison.OrdinalIgnoreCase))
        {
            var show = string.IsNullOrWhiteSpace(item.ShowTitle) ? item.Title : item.ShowTitle;
            var season = item.SeasonNumber.HasValue ? $" Season {item.SeasonNumber}" : string.Empty;
            return $"{show}{season} [Season]";
        }

        var year = item.Year.HasValue ? $" ({item.Year})" : string.Empty;
        return $"{item.Title}{year} [{item.MediaType}]";
    }

    private static ChronologyResult ToResult(CollectionOrderingItemDto item, decimal sortOrder) => new()
    {
        LocalId = item.LocalId,
        TmdbId = item.TmdbId,
        ImdbId = item.ImdbId,
        MediaType = item.MediaType,
        SortOrder = sortOrder
    };

    private class OrderResult
    {
        public int[]? Order { get; set; }
    }
}
