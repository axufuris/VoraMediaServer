using Vora.Application.Ai;
using Vora.Application.Settings;
using Vora.Domain.Entities.Ai;

namespace Vora.Application.Tests.Ai;

public class OpenAiClientTests
{
    private readonly IAiEndpoint _endpoint = Substitute.For<IAiEndpoint>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IAiUsageRepository _usage = Substitute.For<IAiUsageRepository>();

    public OpenAiClientTests()
    {
        _endpoint.ChatAsync(Arg.Any<AiConnection>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new AiChatReply("""{"ok":true}""", 30, 12, 42));
        _endpoint.EmbedAsync(Arg.Any<AiConnection>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => new AiEmbeddingReply(ci.Arg<IReadOnlyList<string>>().Select(_ => (float[]?)new float[AiConnection.StoredDimensions]).ToList(), 1536, 9, 9));
    }

    private OpenAiClient Client(params (string Key, string Value)[] saved)
    {
        _settings.GetAllPluginSettingsAsync(AiConnection.PluginId).Returns(StubAiServer.Settings(saved));
        return new OpenAiClient(_endpoint, _settings, _usage);
    }

    private static (string, string) Key => (AiConnection.ApiKeyKey, "sk-example");
    private static (string, string) Ollama => (AiConnection.BaseUrlKey, "http://ollama.example:11434/v1");

    private Task<AiChatReply> ChatWith(string model) =>
        _endpoint.Received(1).ChatAsync(Arg.Any<AiConnection>(), Arg.Is(model), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<double?>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Without_a_key_OpenAI_is_never_called()
    {
        var client = Client();

        (await client.IsConfiguredAsync()).Should().BeFalse();
        (await client.GetEmbeddingModelAsync()).Should().BeNull();
        (await client.CompleteJsonAsync("openai_list", "List the films", TestContext.Current.CancellationToken)).Should().BeNull();
        (await client.EmbedAsync("openai_recommendations", new[] { "Arrival" }, TestContext.Current.CancellationToken)).Should().BeNull();

        _endpoint.ReceivedCalls().Should().BeEmpty();
        await _usage.DidNotReceive().LogAiUsageAsync(Arg.Any<AiUsageLog>());
    }

    [Fact]
    public async Task A_chat_is_answered_logged_against_its_feature_and_given_the_house_rules()
    {
        var profileId = Guid.NewGuid();
        var client = Client(Key, (AiConnection.GuardrailsKey, "Exclude adult content"));

        var answer = await client.CompleteJsonAsync("openai_list", "List the films", TestContext.Current.CancellationToken, temperature: 0.2, profileId: profileId);

        answer.Should().Be("""{"ok":true}""");
        await _endpoint.Received(1).ChatAsync(Arg.Is<AiConnection>(c => c.IsOpenAi), Arg.Is("gpt-4o-mini"), Arg.Is<string?>("Exclude adult content"), Arg.Is("List the films"), Arg.Is<double?>(t => t == 0.2), Arg.Any<CancellationToken>());
        await _usage.Received(1).LogAiUsageAsync(Arg.Is<AiUsageLog>(l =>
            l.PluginId == "openai_list" && l.ModelUsed == "gpt-4o-mini" && l.PromptTokens == 30 && l.CompletionTokens == 12 && l.TotalTokens == 42 && l.ProfileId == profileId));
    }

    [Fact]
    public async Task Each_feature_asks_the_model_chosen_for_it()
    {
        var client = Client(Key,
            (AiConnection.ChatModelKey, "gpt-4.1-mini"),
            (AiConnection.CollectionsModelKey, "gpt-5"),
            (AiConnection.PlaylistsModelKey, "gpt-4o"));

        await client.CompleteJsonAsync("openai_recommendations", "For you", TestContext.Current.CancellationToken);
        await client.CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken, modelSettingKey: AiConnection.CollectionsModelKey);
        await client.CompleteJsonAsync("openai_music_playlists", "Playlist", TestContext.Current.CancellationToken, modelSettingKey: AiConnection.PlaylistsModelKey);

        await ChatWith("gpt-4.1-mini");
        await ChatWith("gpt-5");
        await ChatWith("gpt-4o");
    }

    [Fact]
    public async Task A_feature_with_no_model_of_its_own_uses_the_recommendations_one()
    {
        var client = Client(Key, (AiConnection.ChatModelKey, "gpt-4.1-mini"));

        await client.CompleteJsonAsync("openai_music_playlists", "Playlist", TestContext.Current.CancellationToken, modelSettingKey: AiConnection.PlaylistsModelKey);

        await ChatWith("gpt-4.1-mini");
    }

    [Fact]
    public async Task The_monthly_limit_stops_a_chat_before_it_is_sent()
    {
        _usage.GetMonthlyTokenUsageAsync().Returns(1_000_000);
        var client = Client(Key, (AiConnection.MonthlyTokenLimitKey, "1000000"));

        var act = () => client.CompleteJsonAsync("openai_recommendations", "For you", TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be("AI monthly token limit reached (1,000,000 / 1,000,000 tokens). Raise 'Monthly Token Limit' in AI Settings or wait until next month.");
        _endpoint.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task The_monthly_limit_stops_an_embedding_before_it_is_sent()
    {
        _usage.GetMonthlyTokenUsageAsync().Returns(500_001);
        var client = Client(Key, (AiConnection.MonthlyTokenLimitKey, "500000"));

        var act = () => client.EmbedAsync("openai_recommendations", new[] { "Arrival" }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _endpoint.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Under_the_limit_or_with_none_requests_go_through()
    {
        _usage.GetMonthlyTokenUsageAsync().Returns(499_999);

        (await Client(Key, (AiConnection.MonthlyTokenLimitKey, "500000")).CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken)).Should().NotBeNull();
        (await Client(Key, (AiConnection.MonthlyTokenLimitKey, "0")).CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_answer_with_no_content_is_nothing_and_is_not_logged()
    {
        _endpoint.ChatAsync(Arg.Any<AiConnection>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new AiChatReply(null, 0, 0, 0));

        (await Client(Key).CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken)).Should().BeNull();

        await _usage.DidNotReceive().LogAiUsageAsync(Arg.Any<AiUsageLog>());
    }

    [Fact]
    public async Task Vectors_come_from_the_embedding_model_and_are_logged_against_it()
    {
        var client = Client(Key);

        var vectors = await client.EmbedAsync("openai_music_playlists", new[] { "One", "Two" }, TestContext.Current.CancellationToken);

        vectors.Should().HaveCount(2);
        await _endpoint.Received(1).EmbedAsync(Arg.Any<AiConnection>(), Arg.Is("text-embedding-3-small"), Arg.Is<IReadOnlyList<string>>(l => l.Count == 2), Arg.Any<CancellationToken>());
        await _usage.Received(1).LogAiUsageAsync(Arg.Is<AiUsageLog>(l => l.PluginId == "openai_music_playlists" && l.ModelUsed == "text-embedding-3-small" && l.TotalTokens == 9));
        (await client.GetEmbeddingModelAsync()).Should().Be("text-embedding-3-small");
    }

    [Fact]
    public async Task Nothing_to_embed_is_answered_without_reading_the_settings()
    {
        var vectors = await new OpenAiClient(_endpoint, _settings, _usage).EmbedAsync("openai_recommendations", Array.Empty<string>(), TestContext.Current.CancellationToken);

        vectors.Should().BeEmpty();
        await _settings.DidNotReceive().GetAllPluginSettingsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task A_server_of_your_own_chats_with_the_model_named_for_it_and_one_request_at_a_time()
    {
        var client = Client(Ollama, (AiConnection.ChatModelKey, "llama3.1"), (AiConnection.EmbeddingModelKey, "nomic-embed-text"));

        (await client.IsConfiguredAsync()).Should().BeTrue();
        (await client.GetParallelismAsync()).Should().Be(1);
        (await client.GetEmbeddingModelAsync()).Should().Be("nomic-embed-text");
        await client.CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken, modelSettingKey: AiConnection.CollectionsModelKey);
        await client.EmbedAsync("openai_recommendations", new[] { "Arrival" }, TestContext.Current.CancellationToken);

        await _endpoint.Received(1).ChatAsync(Arg.Is<AiConnection>(c => !c.IsOpenAi && c.ApiKey == null), Arg.Is("llama3.1"), Arg.Is<string?>(rules => rules == null), Arg.Is("List"), Arg.Is<double?>(t => t == null), Arg.Any<CancellationToken>());
        await _endpoint.Received(1).EmbedAsync(Arg.Any<AiConnection>(), Arg.Is("nomic-embed-text"), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_server_of_your_own_with_no_models_chosen_is_not_configured()
    {
        var client = Client(Ollama);

        (await client.IsConfiguredAsync()).Should().BeFalse();
        (await client.GetEmbeddingModelAsync()).Should().BeNull();
        (await client.CompleteJsonAsync("openai_list", "List", TestContext.Current.CancellationToken)).Should().BeNull();
        (await client.EmbedAsync("openai_recommendations", new[] { "Arrival" }, TestContext.Current.CancellationToken)).Should().BeNull();
        _endpoint.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAI_takes_six_requests_at_once()
    {
        (await Client(Key).GetParallelismAsync()).Should().Be(6);
    }
}
