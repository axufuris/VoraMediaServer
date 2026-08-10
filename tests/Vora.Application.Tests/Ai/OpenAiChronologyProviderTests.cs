using Vora.Application.Ai;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Ai;

public class OpenAiChronologyProviderTests
{
    private static CollectionOrderingItemDto Item(int index, Guid id, string title) =>
        new() { Index = index, LocalId = id, Title = title, MediaType = "Movie" };

    private static CollectionOrderingItemDto SeasonItem(int index, Guid id, string show, int seasonNumber) =>
        new() { Index = index, LocalId = id, Title = $"{show} S{seasonNumber}", ShowTitle = show, SeasonNumber = seasonNumber, MediaType = "Season" };

    private static IOpenAiClient AiReturning(params string[] responses)
    {
        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns(responses[0], responses.Skip(1).ToArray());
        return openAi;
    }

    [Fact]
    public async Task Skips_the_ai_entirely_when_every_item_already_has_a_cached_set_year()
    {
        var iron = Guid.NewGuid();
        var endgame = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            new() { Index = 0, LocalId = endgame, Title = "Endgame", MediaType = "Movie", KnownSetYear = 2023.0 },
            new() { Index = 1, LocalId = iron, Title = "Iron Man", MediaType = "Movie", KnownSetYear = 2010.0 },
        };

        var openAi = Substitute.For<IOpenAiClient>();
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", null, items, TestContext.Current.CancellationToken);

        await openAi.DidNotReceive().CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { iron, endgame }, ordered);
        Assert.Equal(2010.0, result.Single(r => r.LocalId == iron).SetYear);
        Assert.Equal(2023.0, result.Single(r => r.LocalId == endgame).SetYear);
    }

    [Fact]
    public async Task Scores_only_the_items_that_lack_a_cached_set_year()
    {
        var cached = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            new() { Index = 0, LocalId = cached, Title = "First Avenger", MediaType = "Movie", KnownSetYear = 1943.0 },
            new() { Index = 1, LocalId = fresh, Title = "The Avengers", MediaType = "Movie" },
        };

        var openAi = AiReturning("{\"items\":[{\"index\":1,\"setYear\":2012}]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { cached, fresh }, ordered);
        Assert.Equal(1943.0, result.Single(r => r.LocalId == cached).SetYear);
        Assert.Equal(2012.0, result.Single(r => r.LocalId == fresh).SetYear);
    }

    [Fact]
    public async Task Leaves_the_set_year_null_for_an_item_the_ai_never_scored()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            new() { Index = 0, LocalId = a, Title = "A", Year = 1990, MediaType = "Movie" },
            new() { Index = 1, LocalId = b, Title = "B", Year = 2020, MediaType = "Movie" },
        };

        var openAi = AiReturning("{\"items\":[{\"index\":1,\"setYear\":2020}]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        Assert.Null(result.Single(r => r.LocalId == a).SetYear);
        Assert.Equal(2020.0, result.Single(r => r.LocalId == b).SetYear);
    }

    [Fact]
    public async Task Verification_pass_corrects_a_newly_scored_item_that_was_misplaced()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto> { Item(0, a, "A"), Item(1, b, "B"), Item(2, c, "C") };

        var openAi = AiReturning(
            "{\"items\":[{\"index\":0,\"setYear\":2000},{\"index\":1,\"setYear\":2050},{\"index\":2,\"setYear\":2010}]}",
            "{\"items\":[{\"index\":1,\"setYear\":1995}]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { b, a, c }, ordered);
        Assert.Equal(1995.0, result.Single(r => r.LocalId == b).SetYear);
    }

    [Fact]
    public async Task Verification_pass_breaks_a_same_year_tie_into_distinct_ordered_fractions()
    {
        var blackWidow = Guid.NewGuid();
        var civilWar = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            Item(0, blackWidow, "Black Widow"),
            Item(1, civilWar, "Captain America: Civil War"),
        };

        var openAi = AiReturning(
            "{\"items\":[{\"index\":0,\"setYear\":2016.1},{\"index\":1,\"setYear\":2016.1}]}",
            "{\"items\":[{\"index\":0,\"setYear\":2016.6},{\"index\":1,\"setYear\":2016.3}]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { civilWar, blackWidow }, ordered);
        Assert.NotEqual(result.Single(r => r.LocalId == civilWar).SetYear, result.Single(r => r.LocalId == blackWidow).SetYear);
    }

    [Fact]
    public async Task No_two_items_keep_the_same_set_year_even_if_the_ai_leaves_a_tie()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto> { Item(0, a, "A"), Item(1, b, "B") };

        var openAi = AiReturning(
            "{\"items\":[{\"index\":0,\"setYear\":2016.1},{\"index\":1,\"setYear\":2016.1}]}",
            "{\"items\":[]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        Assert.NotEqual(result.Single(r => r.LocalId == a).SetYear, result.Single(r => r.LocalId == b).SetYear);
    }

    [Fact]
    public async Task Repairs_a_season_the_model_scored_decades_off_from_its_show()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var filler = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            SeasonItem(0, s1, "Agents of S.H.I.E.L.D.", 1),
            SeasonItem(1, s2, "Agents of S.H.I.E.L.D.", 2),
            Item(2, filler, "A Filler Film"),
        };

        var openAi = AiReturning(
            "{\"items\":[{\"index\":0,\"setYear\":2013},{\"index\":1,\"setYear\":2050},{\"index\":2,\"setYear\":2030}]}",
            "{\"items\":[]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { s1, s2, filler }, ordered);
    }

    [Fact]
    public async Task Leaves_a_show_whose_seasons_legitimately_progress_year_over_year()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var s3 = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            SeasonItem(0, s1, "The Expanse", 1),
            SeasonItem(1, s2, "The Expanse", 2),
            SeasonItem(2, s3, "The Expanse", 3),
        };

        var openAi = AiReturning(
            "{\"items\":[{\"index\":0,\"setYear\":2350},{\"index\":1,\"setYear\":2351},{\"index\":2,\"setYear\":2352}]}",
            "{\"items\":[]}");
        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("The Expanse", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { s1, s2, s3 }, ordered);
        Assert.Equal(2350.0, result.Single(r => r.LocalId == s1).SetYear);
        Assert.Equal(2351.0, result.Single(r => r.LocalId == s2).SetYear);
        Assert.Equal(2352.0, result.Single(r => r.LocalId == s3).SetYear);
    }

    [Fact]
    public async Task Orders_by_decimal_set_year_breaking_same_year_ties()
    {
        var avengers = Guid.NewGuid();
        var ironMan3 = Guid.NewGuid();
        var firstAvenger = Guid.NewGuid();

        var items = new List<CollectionOrderingItemDto>
        {
            Item(0, ironMan3, "Iron Man 3"),        // set late 2012
            Item(1, firstAvenger, "First Avenger"), // set 1943
            Item(2, avengers, "The Avengers"),      // set mid 2012
        };

        // Model returns them out of order with decimal setYears; the provider
        // must sort by setYear so 2012.4 (Avengers) precedes 2012.9 (Iron Man 3).
        var json = "{\"items\":[" +
                   "{\"index\":0,\"setYear\":2012.9}," +
                   "{\"index\":2,\"setYear\":2012.4}," +
                   "{\"index\":1,\"setYear\":1943.0}]}";

        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns(json);

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", "MCU", items, TestContext.Current.CancellationToken);

        // SortOrder ascending should be: First Avenger, The Avengers, Iron Man 3
        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { firstAvenger, avengers, ironMan3 }, ordered);
    }

    [Fact]
    public async Task Retries_to_score_items_the_first_response_dropped()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto> { Item(0, a, "A"), Item(1, b, "B"), Item(2, c, "C") };

        var openAi = Substitute.For<IOpenAiClient>();
        // First response drops index 1; the retry scores it. Merged by setYear
        // the order is A(2000), B(2005), C(2010) — nothing left unscored.
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns(
                "{\"items\":[{\"index\":0,\"setYear\":2000},{\"index\":2,\"setYear\":2010}]}",
                "{\"items\":[{\"index\":1,\"setYear\":2005}]}");

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { a, b, c }, ordered);
    }

    [Fact]
    public async Task Falls_back_to_release_year_only_when_ai_never_scores_an_item()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto>
        {
            new() { Index = 0, LocalId = a, Title = "A", Year = 1990, MediaType = "Movie" },
            new() { Index = 1, LocalId = b, Title = "B", Year = 2020, MediaType = "Movie" },
        };

        var openAi = Substitute.For<IOpenAiClient>();
        // Every attempt scores only index 1; index 0 is never scored and must
        // fall back to its release year (1990, so it still sorts first).
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns("{\"items\":[{\"index\":1,\"setYear\":2020}]}");

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { a, b }, ordered);
    }
}
