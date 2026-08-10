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

    // Ask for setYears in modestly-sized batches. A model reliably scores a
    // couple dozen items in one JSON response but starts dropping entries from a
    // long exhaustive list. Because each setYear is an absolute per-item value,
    // batches can be scored independently and merged into one global order.
    private const int BatchSize = 25;
    private const int MaxAttemptsPerBatch = 3;

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => new List<PluginSettingDefinitionDto>();

    public async Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            return new List<ChronologyResult>();
        }

        var description = string.IsNullOrWhiteSpace(externalId) ? collectionName : externalId;
        var byIndex = items.ToDictionary(i => i.Index);
        var setYears = new Dictionary<int, double>();

        foreach (var batch in items.Chunk(BatchSize))
        {
            for (var attempt = 0; attempt < MaxAttemptsPerBatch; attempt++)
            {
                var pending = batch.Where(i => !setYears.ContainsKey(i.Index)).ToList();
                if (pending.Count == 0)
                {
                    break;
                }

                var json = await openAi.CompleteJsonAsync(Id, BuildPrompt(description, pending), cancellationToken, temperature: 0.2, modelSettingKey: "collections_model");
                var parsed = TryParse(json);
                if (parsed?.Items == null)
                {
                    break;
                }

                var scoredSomething = false;
                foreach (var entry in parsed.Items)
                {
                    if (entry.SetYear.HasValue && byIndex.ContainsKey(entry.Index) && setYears.TryAdd(entry.Index, entry.SetYear.Value))
                    {
                        scoredSomething = true;
                    }
                }

                // Nothing new landed — retrying the same batch won't help.
                if (!scoredSomething)
                {
                    break;
                }
            }
        }

        // Sort by the AI's in-universe setYear. Only items the AI never scored
        // after retries fall back to their release year, then original position.
        var ranked = items
            .Select((item, position) => (
                SetYear: setYears.TryGetValue(item.Index, out var sy) ? sy : (item.Year ?? double.MaxValue),
                Position: position,
                Item: item))
            .OrderBy(r => r.SetYear)
            .ThenBy(r => r.Position)
            .ToList();

        var results = new List<ChronologyResult>();
        decimal sortOrder = 1;
        foreach (var r in ranked)
        {
            results.Add(ToResult(r.Item, sortOrder++));
        }

        return results;
    }

    private static string BuildPrompt(string description, IReadOnlyList<CollectionOrderingItemDto> batch)
    {
        var lines = batch.Select(i => $"{i.Index}: {DescribeItem(i)}");

        return
            $"You are ordering a media collection. It is described as: \"{description}\".\n" +
            "Here is a batch of its items, one per line as `index: Title (ReleaseYear) [Type]` — the year shown is the RELEASE " +
            "year:\n" +
            string.Join("\n", lines) + "\n\n" +
            "Unless the description explicitly asks for release order, place each by the IN-UNIVERSE story timeline. For EVERY " +
            "index above give a DECIMAL setYear whose whole part is the year the story PRIMARILY takes place within the fictional " +
            "world and whose fractional part sequences events WITHIN that year (e.g. an event early in 2012 is 2012.1, one later in " +
            "2012 is 2012.8). Use the fraction to break same-year ties — two films both set in 2012 where one clearly follows the " +
            "other get 2012.3 and 2012.7. The setYear is frequently NOT the release year: an origin story, prequel, period piece, " +
            "or flashback is set earlier than a film released before it — a 1940s-set wartime origin comes very early, a 1990s-set " +
            "prequel comes before later-released present-day films. This also applies to a title released years AFTER the events it " +
            "depicts — a prequel or a gap-filler set between two earlier stories takes its story year, not its release year. For an " +
            "item spanning multiple periods, use its main present-day storyline. Use the widely-published in-universe timeline for " +
            "established franchises.\n" +
            "Return ONLY valid JSON of the form {\"items\": [{\"index\": <index>, \"setYear\": <decimal>}, ...]}. You MUST give a " +
            "setYear for EVERY index shown above — never omit, invent, or duplicate an index.";
    }

    private static OrderResult? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OrderResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
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
