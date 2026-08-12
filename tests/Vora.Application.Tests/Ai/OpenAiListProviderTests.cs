using Vora.Application.Ai;

namespace Vora.Application.Tests.Ai;

public class OpenAiListProviderTests
{
    private static IOpenAiClient AiReturning(params string[] responses)
    {
        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns(responses[0], responses.Skip(1).ToArray());
        return openAi;
    }

    [Fact]
    public async Task Completeness_pass_adds_titles_the_first_list_missed()
    {
        var openAi = AiReturning(
            "{\"items\":[{\"type\":\"movie\",\"title\":\"Iron Man\",\"year\":2008}]}",
            "{\"items\":[{\"type\":\"movie\",\"title\":\"The Avengers\",\"year\":2012}]}",
            "{\"items\":[]}");

        var result = await new OpenAiListProvider(openAi).FetchItemsAsync("MCU");

        var titles = result.Select(r => r.Title).ToList();
        Assert.Contains("Iron Man", titles);
        Assert.Contains("The Avengers", titles);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Completeness_pass_does_not_duplicate_items_already_listed()
    {
        var openAi = AiReturning(
            "{\"items\":[{\"type\":\"movie\",\"title\":\"Iron Man\",\"year\":2008}]}",
            "{\"items\":[{\"type\":\"movie\",\"title\":\"Iron Man\",\"year\":2008},{\"type\":\"season\",\"show\":\"Loki\",\"season\":1}]}",
            "{\"items\":[]}");

        var result = await new OpenAiListProvider(openAi).FetchItemsAsync("MCU");

        Assert.Equal(1, result.Count(r => r.Title == "Iron Man"));
        Assert.Contains(result, r => r.MediaType == "Season" && r.ShowTitle == "Loki" && r.SeasonNumber == 1);
    }

    [Fact]
    public async Task Generates_a_universe_description_from_the_ai_response()
    {
        var openAi = AiReturning("{\"description\":\"A shared superhero universe of interconnected films and series.\"}");

        var result = await new OpenAiListProvider(openAi).GenerateDescriptionAsync("Marvel Cinematic Universe");

        Assert.Equal("A shared superhero universe of interconnected films and series.", result);
    }

    [Fact]
    public async Task Does_not_call_the_ai_for_a_blank_description_request()
    {
        var openAi = Substitute.For<IOpenAiClient>();

        var result = await new OpenAiListProvider(openAi).GenerateDescriptionAsync("   ");

        Assert.Null(result);
        await openAi.DidNotReceiveWithAnyArgs().CompleteJsonAsync(default!, default!, default, default, default);
    }

    [Fact]
    public async Task Skips_completeness_when_the_first_list_is_empty()
    {
        var openAi = Substitute.For<IOpenAiClient>();
        openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>())
            .Returns("{\"items\":[]}");

        var result = await new OpenAiListProvider(openAi).FetchItemsAsync("x");

        Assert.Empty(result);
        await openAi.Received(1).CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>());
    }
}
