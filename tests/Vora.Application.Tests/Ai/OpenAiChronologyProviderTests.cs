using Vora.Application.Ai;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Ai;

public class OpenAiChronologyProviderTests
{
    private static CollectionOrderingItemDto Item(int index, Guid id, string title) =>
        new() { Index = index, LocalId = id, Title = title, MediaType = "Movie" };

    private static CollectionOrderingItemDto Season(int index, Guid id, string show, int seasonNumber) =>
        new() { Index = index, LocalId = id, MediaType = "Season", ShowTitle = show, SeasonNumber = seasonNumber };

    [Fact]
    public async Task Repairs_a_season_the_model_scored_decades_off_from_its_show()
    {
        var movie1946 = Guid.NewGuid();
        var carterS1 = Guid.NewGuid();
        var carterS2 = Guid.NewGuid();
        var movie2016 = Guid.NewGuid();

        var items = new List<CollectionOrderingItemDto>
        {
            Item(0, movie1946, "Old Movie"),
            Season(1, carterS1, "Agent Carter", 1),
            Season(2, carterS2, "Agent Carter", 2),
            Item(3, movie2016, "New Movie"),
        };

        var openAi = Substitute.For<IOpenAiClient>();
        // The model scores Agent Carter S1 correctly (1946) but S2 at its 2016
        // release year. The season guard must pull S2 back next to S1.
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns("{\"items\":[{\"index\":0,\"setYear\":1946},{\"index\":1,\"setYear\":1946.1}," +
                     "{\"index\":2,\"setYear\":2016.0},{\"index\":3,\"setYear\":2016.5}]}");

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", "MCU", items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { movie1946, carterS1, carterS2, movie2016 }, ordered);
    }

    [Fact]
    public async Task Leaves_a_show_that_legitimately_spans_years_untouched()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        var mid = Guid.NewGuid();

        var items = new List<CollectionOrderingItemDto>
        {
            Season(0, s1, "SHIELD", 1),      // 2013
            Item(1, mid, "A 2014 Film"),     // 2014 — between the two seasons
            Season(2, s2, "SHIELD", 2),      // 2015
        };

        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns("{\"items\":[{\"index\":0,\"setYear\":2013.5},{\"index\":1,\"setYear\":2014.5},{\"index\":2,\"setYear\":2015.5}]}");

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("MCU", "MCU", items, TestContext.Current.CancellationToken);

        // SHIELD S2 (2015) stays after the 2014 film — the guard must not cluster it back next to S1.
        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { s1, mid, s2 }, ordered);
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
