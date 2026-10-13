using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;
using Vora.Application.Settings;

namespace Vora.Application.Tests.Ai;

public class AiConnectionTesterTests
{
    private const string OllamaUrl = "http://ollama.example:11434";

    private readonly ISystemSettingsRepository _saved = Substitute.For<ISystemSettingsRepository>();

    private AiConnectionTester Tester(StubAiServer server) =>
        new(new AiEndpoint(server, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiEndpoint>.Instance), _saved);

    private static float[] Vector(int length) => Enumerable.Repeat(0.5f, length).ToArray();

    [Fact]
    public async Task A_key_openai_accepts_passes_and_is_sent_as_a_bearer_token()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("gpt-4o-mini", "gpt-5", "text-embedding-3-small"));

        var result = await Tester(server).TestAsync(StubAiServer.Settings((AiConnection.ApiKeyKey, " sk-test ")), TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("OpenAI accepted the API key.");
        var call = server.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.openai.com/v1/models");
        call.Authorization.Should().Be("Bearer sk-test");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "OpenAI rejected the API key.")]
    [InlineData(HttpStatusCode.TooManyRequests, "OpenAI accepted the key but refused the request: check that the account has billing set up and credit left.")]
    [InlineData(HttpStatusCode.BadGateway, "Unexpected response from OpenAI (HTTP 502).")]
    public async Task A_refused_key_says_why(HttpStatusCode status, string message)
    {
        var server = new StubAiServer(_ => StubAiServer.Json("{}", status));

        var result = await Tester(server).TestAsync(StubAiServer.Settings((AiConnection.ApiKeyKey, "sk-test")), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Be(message);
    }

    [Fact]
    public async Task No_key_is_not_sent_anywhere()
    {
        var server = new StubAiServer(_ => StubAiServer.Models());

        var result = await Tester(server).TestAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Enter an API key first.");
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_model_OpenAI_does_not_list_is_named()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("gpt-4o-mini", "text-embedding-3-small"));
        var settings = StubAiServer.Settings((AiConnection.ApiKeyKey, "sk-test"), (AiConnection.CollectionsModelKey, "gpt-5"));

        var result = await Tester(server).TestAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Connected, but OpenAI has no model named \"gpt-5\".");
    }

    [Fact]
    public async Task An_address_that_is_not_one_is_not_sent_anywhere()
    {
        var server = new StubAiServer(_ => StubAiServer.Models());

        var result = await Tester(server).TestAsync(StubAiServer.Settings((AiConnection.BaseUrlKey, "ftp://ai.example.com")), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("Enter the server address as http:// or https://");
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_server_of_your_own_connects_without_a_key_and_asks_for_models()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("llama3.1", "nomic-embed-text"));

        var result = await Tester(server).TestAsync(StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl)), TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected. 2 models found. Choose which to use below.");
        var call = server.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("http://ollama.example:11434/v1/models");
        call.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task A_chosen_model_the_server_does_not_have_is_named()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("llama3.1", "nomic-embed-text"));
        var settings = StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ChatModelKey, "llama3.1"), (AiConnection.PlaylistsModelKey, "gpt-4o-mini"));

        var result = await Tester(server).TestAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Connected, but the server at ollama.example:11434 has no model named \"gpt-4o-mini\".");
    }

    [Fact]
    public async Task Chat_alone_passes_and_says_what_stays_off()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("llama3.1"));
        var settings = StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ChatModelKey, "llama3.1"));

        var result = await Tester(server).TestAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected. No embedding model is chosen, so recommendations and AI playlists stay off.");
    }

    [Fact]
    public async Task The_embedding_model_is_tried_before_it_is_trusted()
    {
        var server = new StubAiServer(call => call.Path.EndsWith("/models", StringComparison.Ordinal)
            ? StubAiServer.Models("llama3.1", "nomic-embed-text")
            : StubAiServer.Embeddings((0, Vector(768))));
        var settings = StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ChatModelKey, "llama3.1"), (AiConnection.EmbeddingModelKey, "nomic-embed-text"));

        var result = await Tester(server).TestAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected. The chosen models are ready.");
        server.Calls.Select(c => c.Path).Should().Equal("/v1/models", "/v1/embeddings");
    }

    [Fact]
    public async Task An_embedding_model_too_large_to_store_fails_the_test()
    {
        var server = new StubAiServer(call => call.Path.EndsWith("/models", StringComparison.Ordinal)
            ? StubAiServer.Models("llama3.1", "qwen3-embedding:8b")
            : StubAiServer.Embeddings((0, Vector(4096))));
        var settings = StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ChatModelKey, "llama3.1"), (AiConnection.EmbeddingModelKey, "qwen3-embedding:8b"));

        var result = await Tester(server).TestAsync(settings, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("returns 4,096 numbers per item");
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_says_so()
    {
        var server = new StubAiServer(_ => throw new HttpRequestException("Connection refused"));

        var result = await Tester(server).TestAsync(StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl)), TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("Couldn't reach the AI server at ollama.example:11434.");
    }

    [Fact]
    public async Task The_models_a_form_can_offer_come_from_the_address_typed_into_it()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("llama3.1", "nomic-embed-text"));

        var models = await Tester(server).ListModelsAsync(StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl)), TestContext.Current.CancellationToken);

        models.Error.Should().BeNull();
        models.ChatModels.Should().Equal("llama3.1");
        models.EmbeddingModels.Should().Equal("nomic-embed-text");
    }

    [Fact]
    public async Task A_model_list_that_cannot_be_read_comes_back_as_a_message()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("{}", HttpStatusCode.Unauthorized));

        var models = await Tester(server).ListModelsAsync(StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ApiKeyKey, "wrong")), TestContext.Current.CancellationToken);

        models.Error.Should().Be("The AI server at ollama.example:11434 rejected the API key (401).");
        models.ChatModels.Should().BeEmpty();
    }

    [Fact]
    public async Task Status_reads_the_saved_connection()
    {
        _saved.GetAllPluginSettingsAsync(AiConnection.PluginId).Returns(StubAiServer.Settings((AiConnection.BaseUrlKey, OllamaUrl), (AiConnection.ChatModelKey, "llama3.1")));

        var status = await Tester(new StubAiServer(_ => StubAiServer.Models())).GetStatusAsync();

        status.Configured.Should().BeTrue();
        status.Embeddings.Should().BeFalse();
        status.IsOpenAi.Should().BeFalse();
        status.Host.Should().Be("ollama.example:11434");
    }

    [Fact]
    public async Task Status_on_a_server_that_saved_nothing_is_not_configured()
    {
        var status = await Tester(new StubAiServer(_ => StubAiServer.Models())).GetStatusAsync();

        status.Configured.Should().BeFalse();
        status.IsOpenAi.Should().BeTrue();
    }
}
