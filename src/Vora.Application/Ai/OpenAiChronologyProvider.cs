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
            $"You are ordering a media collection. It is described as: \"{description}\".\n" +
            "Items, one per line as `index: Title (ReleaseYear) [Type]` — the year shown is the RELEASE year:\n" +
            string.Join("\n", lines) + "\n\n" +
            "Unless the description explicitly asks for release order, order by the IN-UNIVERSE story timeline. For EVERY index, " +
            "give a DECIMAL setYear whose whole part is the year the story PRIMARILY takes place within the fictional world and " +
            "whose fractional part sequences events WITHIN that year (e.g. an event early in 2012 is 2012.1, one later in 2012 is " +
            "2012.8). Use the fraction to break ties so same-year items are ordered correctly — e.g. two films both set in 2012 " +
            "where one clearly follows the other get 2012.3 and 2012.7. The setYear is frequently NOT the release year: an origin " +
            "story, prequel, period piece, or flashback is set earlier than a film released before it — a 1940s-set wartime origin " +
            "comes very early, a 1990s-set prequel comes before later-released present-day films. For an item spanning multiple " +
            "periods, use its main present-day storyline. Use the widely-published in-universe timeline for established franchises.\n" +
            "Return ONLY valid JSON of the form {\"items\": [{\"index\": <index>, \"setYear\": <decimal>}, ...]}, listing EVERY " +
            "index above exactly once, ordered by setYear ascending. Never omit, invent, or duplicate an index.";

        var json = await openAi.CompleteJsonAsync(Id, prompt, cancellationToken, temperature: 0.2, modelSettingKey: "collections_model");
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ChronologyResult>();
        }

        OrderResult? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OrderResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new List<ChronologyResult>();
        }

        if (parsed?.Items == null || parsed.Items.Count == 0)
        {
            return new List<ChronologyResult>();
        }

        var byIndex = items.ToDictionary(i => i.Index);
        var seen = new HashSet<int>();
        var ranked = new List<(double SetYear, int Position, CollectionOrderingItemDto Item)>();

        for (var position = 0; position < parsed.Items.Count; position++)
        {
            var entry = parsed.Items[position];
            if (!seen.Add(entry.Index) || !byIndex.TryGetValue(entry.Index, out var item)) continue;
            ranked.Add((entry.SetYear ?? double.MaxValue, position, item));
        }

        var ordered = ranked.OrderBy(r => r.SetYear).ThenBy(r => r.Position).Select(r => r.Item).ToList();

        // Anything the model dropped keeps its original relative order at the end.
        ordered.AddRange(items.Where(i => !seen.Contains(i.Index)));

        var results = new List<ChronologyResult>();
        decimal sortOrder = 1;
        foreach (var item in ordered)
        {
            results.Add(ToResult(item, sortOrder++));
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
        public List<OrderedItem>? Items { get; set; }
    }

    private class OrderedItem
    {
        public int Index { get; set; }
        public double? SetYear { get; set; }
    }
}
