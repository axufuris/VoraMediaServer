using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Ai;
using Vora.Application.Recommendations;
using Vora.Application.Recommendations.Providers;
using Vora.Plugins.Dtos;

namespace Vora.Application.Tests.Recommendations;

// The For You rows used to call OpenAI on their own, which skipped the monthly
// token limit and the guardrails every other AI feature gets. They go through
// the shared client now, so both apply here too.
public class OpenAiRecommendationProviderTests
{
    private const string Model = "text-embedding-3-small";
    private const string Categories = """{"categories":[{"title":"Gritty Heists","description":"Crews and scores.","vibe_keywords":"heist crime"},{"title":"Cozy Sci-Fi","description":"Gentle futures.","vibe_keywords":"warm space"}]}""";

    private readonly IOpenAiRecommendationRepository _repository = Substitute.For<IOpenAiRecommendationRepository>();
    private readonly IOpenAiClient _openAi = Substitute.For<IOpenAiClient>();
    private readonly IAiConnectionTester _tester = Substitute.For<IAiConnectionTester>();
    private readonly Guid _profile = Guid.NewGuid();
    private readonly Guid _movie = Guid.NewGuid();

    public OpenAiRecommendationProviderTests()
    {
        _repository.IsAiEnabledForProfileAsync(_profile).Returns(true);
        _repository.GetRecentWatchHistoryContextAsync(_profile, 5).Returns(new List<string> { "Heat (1995)", "Arrival (2016)" });
        _repository.VectorSearchUnwatchedMediaAsync(_profile, Arg.Any<Guid?>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<string>())
            .Returns(new List<Guid> { _movie });
        _openAi.IsConfiguredAsync().Returns(true);
        _openAi.GetEmbeddingModelAsync().Returns(Model);
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(Categories);
        _openAi.EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IReadOnlyList<string>>().Select(_ => (float[]?)new[] { 0.5f }).ToList());
    }

    private OpenAiRecommendationProvider Provider() =>
        new(_repository, _openAi, _tester, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Categories_come_from_the_shared_client_with_the_recommendations_model_and_the_viewers_profile()
    {
        var lists = (await Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken)).ToList();

        lists.Select(l => l.Title).Should().Equal("AI: Gritty Heists", "AI: Cozy Sci-Fi");
        lists.Select(l => l.Weight).Should().Equal(90, 89);
        lists[0].LocalItemIds.Should().Equal(_movie);
        await _openAi.Received(1).CompleteJsonAsync(
            Arg.Is(AiConnection.PluginId),
            Arg.Is<string>(p => p.Contains("Heat (1995), Arrival (2016)")),
            Arg.Any<CancellationToken>(),
            Arg.Is<double?>(t => t == null),
            Arg.Is<string?>(key => key == null),
            Arg.Is<Guid?>(id => id == _profile));
    }

    [Fact]
    public async Task Every_category_is_embedded_in_one_request_and_searched_among_the_current_models_vectors()
    {
        await Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);

        await _openAi.Received(1).EmbedAsync(
            AiConnection.PluginId,
            Arg.Is<IReadOnlyList<string>>(l => l.Count == 2 && l[0] == "Gritty Heists Crews and scores. heist crime"),
            Arg.Any<CancellationToken>());
        await _repository.Received(2).VectorSearchUnwatchedMediaAsync(_profile, Arg.Any<Guid?>(), Arg.Any<float[]>(), 15, Model);
    }

    [Fact]
    public async Task The_monthly_limit_reaches_the_caller_and_nothing_is_searched()
    {
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns<Task<string?>>(_ => throw new InvalidOperationException("AI monthly token limit reached"));

        var act = () => Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().VectorSearchUnwatchedMediaAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("Sorry, I can't help with that.")]
    [InlineData("""{"categories":"none"}""")]
    [InlineData("""{"categories":[{"description":"No title"}]}""")]
    [InlineData(null)]
    public async Task An_answer_that_is_not_the_json_asked_for_gives_no_rows(string? answer)
    {
        _openAi.CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>())
            .Returns(answer);

        var lists = await Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);

        lists.Should().BeEmpty();
        await _openAi.DidNotReceive().EmbedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_an_embedding_model_nothing_is_asked()
    {
        _openAi.GetEmbeddingModelAsync().Returns((string?)null);

        var lists = await Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);

        lists.Should().BeEmpty();
        await _openAi.DidNotReceive().CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task A_profile_that_turned_ai_recommendations_off_is_left_alone()
    {
        _repository.IsAiEnabledForProfileAsync(_profile).Returns(false);

        (await Provider().GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken)).Should().BeEmpty();

        await _openAi.DidNotReceive().IsConfiguredAsync();
    }

    [Fact]
    public async Task Rows_are_kept_so_a_second_look_costs_nothing()
    {
        var provider = Provider();

        await provider.GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);
        await provider.GetRecommendationsAsync(_profile, null, TestContext.Current.CancellationToken);

        await _openAi.Received(1).CompleteJsonAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<double?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task The_connection_test_is_the_shared_one()
    {
        var settings = new Dictionary<string, string> { [AiConnection.ApiKeyKey] = "sk-test" };
        _tester.TestAsync(settings, Arg.Any<CancellationToken>()).Returns(PluginConnectionTestResult.Ok("OpenAI accepted the API key."));

        var result = await Provider().TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
    }

    // A server that never saved these settings is shown, and so saves, exactly
    // what it was already running on.
    [Fact]
    public void The_settings_default_to_OpenAI_and_the_models_already_in_use()
    {
        var defaults = Provider().GetSettingDefinitions().ToDictionary(d => d.Key, d => d.DefaultValue);

        defaults[AiConnection.BaseUrlKey].Should().Be("https://api.openai.com/v1");
        defaults[AiConnection.ApiKeyKey].Should().BeEmpty();
        defaults[AiConnection.ChatModelKey].Should().Be("gpt-4o-mini");
        defaults[AiConnection.CollectionsModelKey].Should().Be("gpt-5");
        defaults[AiConnection.PlaylistsModelKey].Should().BeEmpty();
        defaults[AiConnection.EmbeddingModelKey].Should().Be("text-embedding-3-small");
        defaults["schedule_time"].Should().Be("02:00");
        defaults[AiConnection.MonthlyTokenLimitKey].Should().Be("0");
    }

    [Fact]
    public void Each_of_the_three_features_and_the_embeddings_has_a_model_setting()
    {
        var models = Provider().GetSettingDefinitions()
            .Where(d => d.Type == OpenAiRecommendationProvider.ModelSettingType)
            .Select(d => d.Key);

        models.Should().Equal(AiConnection.ChatModelKey, AiConnection.CollectionsModelKey, AiConnection.PlaylistsModelKey, AiConnection.EmbeddingModelKey);
    }
}
