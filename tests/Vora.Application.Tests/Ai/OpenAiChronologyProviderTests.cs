using Vora.Application.Ai;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Ai;

public class OpenAiChronologyProviderTests
{
    private static CollectionOrderingItemDto Item(int index, Guid id, string title) =>
        new() { Index = index, LocalId = id, Title = title, MediaType = "Movie" };

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
    public async Task Appends_indices_the_model_dropped_at_the_end()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var items = new List<CollectionOrderingItemDto> { Item(0, a, "A"), Item(1, b, "B") };

        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns("{\"items\":[{\"index\":1,\"setYear\":2000}]}"); // drops index 0

        var provider = new OpenAiChronologyProvider(openAi);
        var result = await provider.GetChronologicalOrderAsync("c", null, items, TestContext.Current.CancellationToken);

        var ordered = result.OrderBy(r => r.SortOrder).Select(r => r.LocalId!.Value).ToList();
        Assert.Equal(new[] { b, a }, ordered);
    }
}
