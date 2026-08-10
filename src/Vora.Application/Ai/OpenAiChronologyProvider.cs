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

    private const int BatchSize = 25;
    private const int MaxAttemptsPerBatch = 3;
    private const double MaxSeasonGap = 15.0;
    private const double SeasonStep = 1.0;

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

        foreach (var cached in items)
        {
            if (cached.KnownSetYear.HasValue)
            {
                setYears[cached.Index] = cached.KnownSetYear.Value;
            }
        }

        var newlyScored = new HashSet<int>();

        foreach (var batch in items.Chunk(BatchSize))
        {
            for (var attempt = 0; attempt < MaxAttemptsPerBatch; attempt++)
            {
                var pending = batch.Where(i => !setYears.ContainsKey(i.Index)).ToList();
                if (pending.Count == 0)
                {
                    break;
                }

                var json = await openAi.CompleteJsonAsync(Id, BuildScoringPrompt(description, pending), cancellationToken, temperature: 0.2, modelSettingKey: "collections_model");
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
                        newlyScored.Add(entry.Index);
                        scoredSomething = true;
                    }
                }

                if (!scoredSomething)
                {
                    break;
                }
            }
        }

        await VerifyPlacementAsync(description, items, setYears, newlyScored, cancellationToken);

        RepairSeasonYears(items, setYears);
        EnforceDistinctSetYears(items, setYears);

        var ranked = items
            .Select((item, position) => (
                SortKey: setYears.TryGetValue(item.Index, out var sy) ? sy : (item.Year ?? double.MaxValue),
                Position: position,
                Item: item))
            .OrderBy(r => r.SortKey)
            .ThenBy(r => r.Position)
            .ToList();

        var results = new List<ChronologyResult>();
        decimal sortOrder = 1;
        foreach (var r in ranked)
        {
            var stored = setYears.TryGetValue(r.Item.Index, out var fy) ? fy : (double?)null;
            results.Add(ToResult(r.Item, sortOrder++, stored));
        }

        return results;
    }

    private async Task VerifyPlacementAsync(string description, IReadOnlyList<CollectionOrderingItemDto> items, Dictionary<int, double> setYears, HashSet<int> toVerify, CancellationToken cancellationToken)
    {
        var ordered = items
            .Where(i => setYears.ContainsKey(i.Index))
            .OrderBy(i => setYears[i.Index])
            .ToList();

        if (ordered.Count < 2)
        {
            return;
        }

        var tied = setYears
            .GroupBy(kv => kv.Value)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(kv => kv.Key))
            .ToHashSet();

        var review = new HashSet<int>(toVerify);
        review.UnionWith(tied);
        if (review.Count == 0)
        {
            return;
        }

        var json = await openAi.CompleteJsonAsync(Id, BuildVerificationPrompt(description, ordered, setYears, review), cancellationToken, temperature: 0.2, modelSettingKey: "collections_model");
        var parsed = TryParse(json);
        if (parsed?.Items == null)
        {
            return;
        }

        foreach (var entry in parsed.Items)
        {
            if (entry.SetYear.HasValue && review.Contains(entry.Index))
            {
                setYears[entry.Index] = entry.SetYear.Value;
            }
        }
    }

    private static void EnforceDistinctSetYears(IReadOnlyList<CollectionOrderingItemDto> items, Dictionary<int, double> setYears)
    {
        const double epsilon = 0.001;

        var ordered = items
            .Select((item, position) => (item, position))
            .Where(x => setYears.ContainsKey(x.item.Index))
            .OrderBy(x => setYears[x.item.Index])
            .ThenBy(x => x.position)
            .ToList();

        double? previous = null;
        foreach (var (item, _) in ordered)
        {
            var year = setYears[item.Index];
            if (previous.HasValue && year <= previous.Value)
            {
                year = previous.Value + epsilon;
                setYears[item.Index] = year;
            }

            previous = year;
        }
    }

    private static void RepairSeasonYears(IReadOnlyList<CollectionOrderingItemDto> items, Dictionary<int, double> setYears)
    {
        var showGroups = items
            .Where(i => string.Equals(i.MediaType, "Season", StringComparison.OrdinalIgnoreCase)
                && i.SeasonNumber.HasValue
                && !string.IsNullOrWhiteSpace(i.ShowTitle))
            .GroupBy(i => i.ShowTitle!);

        foreach (var group in showGroups)
        {
            var seasons = group.OrderBy(s => s.SeasonNumber!.Value).ToList();
            if (seasons.Count < 2)
            {
                continue;
            }

            double? previous = null;
            foreach (var season in seasons)
            {
                var year = setYears.TryGetValue(season.Index, out var sy) ? sy : (season.Year ?? double.MaxValue);
                if (previous.HasValue && (year < previous.Value || year > previous.Value + MaxSeasonGap))
                {
                    year = previous.Value + SeasonStep;
                    setYears[season.Index] = year;
                }

                previous = year;
            }
        }
    }

    private static string BuildScoringPrompt(string description, IReadOnlyList<CollectionOrderingItemDto> batch)
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
            "2012 is 2012.8). Every setYear MUST be UNIQUE — never give two items the same value. When several items share a year, " +
            "spread them across DISTINCT fractions ordered by their exact in-universe sequence (e.g. three items set in 2016 become " +
            "2016.2, 2016.5 and 2016.8, the one that happens first getting the smallest fraction). The setYear is frequently NOT " +
            "the release year: an origin story, prequel, period piece, " +
            "or flashback is set earlier than a film released before it — a 1940s-set wartime origin comes very early, a 1990s-set " +
            "prequel comes before later-released present-day films. This also applies to a title released years AFTER the events it " +
            "depicts — a prequel or a gap-filler set between two earlier stories takes its story year, not its release year. For an " +
            "item spanning multiple periods, use its main present-day storyline. A television season takes the story year of its " +
            "own episodes, so later seasons of a show never move earlier than their earlier seasons. Use the widely-published " +
            "in-universe timeline for established franchises.\n" +
            "Return ONLY valid JSON of the form {\"items\": [{\"index\": <index>, \"setYear\": <decimal>}, ...]}. You MUST give a " +
            "setYear for EVERY index shown above — never omit, invent, or duplicate an index, and never repeat a setYear value.";
    }

    private static string BuildVerificationPrompt(string description, IReadOnlyList<CollectionOrderingItemDto> ordered, Dictionary<int, double> setYears, HashSet<int> review)
    {
        var lines = ordered.Select(i => $"{i.Index}: {DescribeItem(i)} — setYear {setYears[i.Index]:0.00}");
        var reviewList = string.Join(", ", review.OrderBy(x => x));

        return
            $"You are auditing the in-universe chronological order of a media collection described as: \"{description}\".\n" +
            "Below is the current order, earliest first, one per line as `index: Title (ReleaseYear) [Type] — setYear <value>`:\n" +
            string.Join("\n", lines) + "\n\n" +
            $"Review these indices: {reviewList}. For each, decide whether its setYear puts it at the " +
            "correct point in this in-universe timeline relative to its neighbours. A period piece, prequel, flashback, or origin " +
            "story belongs at its story year, not its release year; a television season belongs with its own show's other seasons " +
            "and never earlier than an earlier season of the same show. Additionally, NO two items may share the same setYear: " +
            "wherever the list above shows a repeated setYear, give those items DISTINCT decimal fractions within that year, " +
            "ordered by their exact in-universe sequence (the one that happens first getting the smaller fraction). If an index is " +
            "out of place or shares a setYear with another, return a corrected DECIMAL setYear for it; if it is already correct and " +
            "unique, omit it.\n" +
            "Return ONLY valid JSON of the form {\"items\": [{\"index\": <index>, \"setYear\": <decimal>}, ...]}, containing only the " +
            "indices you are correcting. Return {\"items\": []} if every reviewed index is already correct and unique.";
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

    private static ChronologyResult ToResult(CollectionOrderingItemDto item, decimal sortOrder, double? setYear) => new()
    {
        LocalId = item.LocalId,
        TmdbId = item.TmdbId,
        ImdbId = item.ImdbId,
        MediaType = item.MediaType,
        SortOrder = sortOrder,
        SetYear = setYear
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
