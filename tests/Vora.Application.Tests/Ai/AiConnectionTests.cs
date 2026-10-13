using Vora.Application.Ai;

namespace Vora.Application.Tests.Ai;

public class AiConnectionTests
{
    [Fact]
    public void A_server_that_never_saved_these_settings_talks_to_OpenAI_with_the_models_it_always_used()
    {
        var connection = StubAiServer.Connection((AiConnection.ApiKeyKey, " sk-example "));

        (connection.BaseUri?.ToString()).Should().Be("https://api.openai.com/v1");
        connection.IsOpenAi.Should().BeTrue();
        connection.ApiKey.Should().Be("sk-example");
        connection.ChatModel().Should().Be("gpt-4o-mini");
        connection.ChatModel(AiConnection.CollectionsModelKey).Should().Be("gpt-4o-mini");
        connection.ChatModel(AiConnection.PlaylistsModelKey).Should().Be("gpt-4o-mini");
        connection.EmbeddingModel.Should().Be("text-embedding-3-small");
        connection.EmbeddingStamp.Should().Be("text-embedding-3-small");
        connection.CanChat.Should().BeTrue();
        connection.CanEmbed.Should().BeTrue();
        connection.Parallelism.Should().Be(6);
        connection.EmbeddingBatchSize.Should().Be(int.MaxValue);
    }

    [Fact]
    public void OpenAI_without_a_key_is_not_usable()
    {
        var connection = new AiConnection(null);

        connection.IsOpenAi.Should().BeTrue();
        connection.IsUsable.Should().BeFalse();
        connection.CanChat.Should().BeFalse();
        connection.CanEmbed.Should().BeFalse();
    }

    [Fact]
    public void Each_feature_uses_its_own_model_and_falls_back_to_the_recommendations_one()
    {
        var connection = StubAiServer.OpenAi(
            (AiConnection.ChatModelKey, "gpt-4.1-mini"),
            (AiConnection.CollectionsModelKey, "gpt-5"),
            (AiConnection.PlaylistsModelKey, "  "));

        connection.ChatModel().Should().Be("gpt-4.1-mini");
        connection.ChatModel(AiConnection.CollectionsModelKey).Should().Be("gpt-5");
        connection.ChatModel(AiConnection.PlaylistsModelKey).Should().Be("gpt-4.1-mini");
    }

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434/v1")]
    [InlineData("http://localhost:11434/", "http://localhost:11434/v1")]
    [InlineData("192.168.1.20:1234", "http://192.168.1.20:1234/v1")]
    [InlineData("https://openrouter.ai/api/v1/", "https://openrouter.ai/api/v1")]
    [InlineData("http://ollama:11434/v1/chat/completions", "http://ollama:11434/v1")]
    [InlineData("http://ollama:11434/v1/embeddings/", "http://ollama:11434/v1")]
    [InlineData("https://ai.example.com/openai/v1?key=ignored", "https://ai.example.com/openai/v1")]
    [InlineData("  https://API.OPENAI.COM/v1  ", "https://api.openai.com/v1")]
    public void An_address_is_tidied_to_where_the_api_starts(string typed, string expected)
    {
        (AiConnection.ParseBaseUrl(typed)?.ToString()).Should().Be(expected);
    }

    [Theory]
    [InlineData("ftp://ai.example.com")]
    [InlineData("http://")]
    [InlineData("http://not a host/v1")]
    public void An_address_that_is_not_http_is_refused(string typed)
    {
        AiConnection.ParseBaseUrl(typed).Should().BeNull();
        StubAiServer.Connection((AiConnection.BaseUrlKey, typed)).IsUsable.Should().BeFalse();
    }

    [Fact]
    public void A_server_other_than_OpenAI_needs_no_key_but_has_no_models_until_they_are_chosen()
    {
        var connection = StubAiServer.Ollama();

        connection.IsOpenAi.Should().BeFalse();
        connection.IsUsable.Should().BeTrue();
        connection.ChatModel().Should().BeNull();
        connection.EmbeddingModel.Should().BeNull();
        connection.CanChat.Should().BeFalse();
        connection.CanEmbed.Should().BeFalse();
        connection.Parallelism.Should().Be(1);
        connection.EmbeddingBatchSize.Should().Be(32);
        connection.Host.Should().Be("ollama.example:11434");
    }

    [Fact]
    public void A_server_other_than_OpenAI_can_chat_without_an_embedding_model()
    {
        var connection = StubAiServer.Ollama((AiConnection.ChatModelKey, "llama3.1"));

        connection.CanChat.Should().BeTrue();
        connection.CanEmbed.Should().BeFalse();
        connection.ChatModel(AiConnection.CollectionsModelKey).Should().Be("llama3.1");
    }

    [Fact]
    public void Requests_go_under_the_address()
    {
        var connection = StubAiServer.Ollama();

        connection.Endpoint("chat/completions").ToString().Should().Be("http://ollama.example:11434/v1/chat/completions");
        connection.Endpoint("/models").ToString().Should().Be("http://ollama.example:11434/v1/models");
    }

    [Fact]
    public void A_model_name_is_cut_to_what_the_column_holds()
    {
        var name = new string('m', 80);

        (AiConnection.Stamp(name)?.Length).Should().Be(AiConnection.ModelStampLength);
        AiConnection.Stamp("  nomic-embed-text ").Should().Be("nomic-embed-text");
        AiConnection.Stamp(" ").Should().BeNull();
    }

    [Fact]
    public void Only_the_model_every_vector_used_to_come_from_counts_for_unstamped_ones()
    {
        AiConnection.IsLegacyStamp("text-embedding-3-small").Should().BeTrue();
        AiConnection.IsLegacyStamp("nomic-embed-text").Should().BeFalse();
        AiConnection.IsLegacyStamp(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("250000", 250000)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData("lots", 0)]
    public void The_monthly_limit_is_read_as_a_number_or_none(string saved, long expected)
    {
        StubAiServer.OpenAi((AiConnection.MonthlyTokenLimitKey, saved)).MonthlyTokenLimit.Should().Be(expected);
    }
}
