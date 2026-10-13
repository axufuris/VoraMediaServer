using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Ai;

namespace Vora.Application.Tests.Ai;

public class AiEndpointTests
{
    private static AiEndpoint Endpoint(StubAiServer server) =>
        new(server, new MemoryCache(new MemoryCacheOptions()), NullLogger<AiEndpoint>.Instance);

    private static float[] Vector(int length, float value = 0.5f) => Enumerable.Repeat(value, length).ToArray();

    [Fact]
    public async Task A_chat_request_to_OpenAI_is_sent_the_way_it_always_was()
    {
        var server = new StubAiServer(_ => StubAiServer.Chat("""{"categories":[]}"""));

        var reply = await Endpoint(server).ChatAsync(StubAiServer.OpenAi(), "gpt-4o-mini", "House rules", "Name three categories", 0.2, TestContext.Current.CancellationToken);

        var call = server.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.openai.com/v1/chat/completions");
        call.Authorization.Should().Be("Bearer sk-example");
        call.Body.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        call.Body.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        call.Body.GetProperty("temperature").GetDouble().Should().Be(0.2);
        call.Has("reasoning_effort").Should().BeFalse();
        var messages = call.Body.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("role").GetString()).Should().Equal("system", "user");
        messages[0].GetProperty("content").GetString().Should().Be("House rules");

        reply.Content.Should().Be("""{"categories":[]}""");
        reply.PromptTokens.Should().Be(12);
        reply.CompletionTokens.Should().Be(8);
        reply.TotalTokens.Should().Be(20);
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("o3-mini")]
    [InlineData("openai/gpt-5-mini")]
    public async Task A_reasoning_model_gets_an_effort_and_no_temperature(string model)
    {
        var server = new StubAiServer(_ => StubAiServer.Chat("{}"));

        await Endpoint(server).ChatAsync(StubAiServer.OpenAi(), model, null, "Order these", 0.2, TestContext.Current.CancellationToken);

        var call = server.Calls.Single();
        call.Body.GetProperty("reasoning_effort").GetString().Should().Be("low");
        call.Has("temperature").Should().BeFalse();
        call.Body.GetProperty("messages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_server_with_no_key_is_sent_no_authorization()
    {
        var server = new StubAiServer(_ => StubAiServer.Chat("{}"));

        await Endpoint(server).ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Hello", null, TestContext.Current.CancellationToken);

        var call = server.Calls.Single();
        call.Url.Should().Be("http://ollama.example:11434/v1/chat/completions");
        call.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task A_server_that_refuses_json_mode_is_asked_again_plainly_and_remembered()
    {
        var server = new StubAiServer(call => call.Has("response_format")
            ? StubAiServer.Json("""{"error":"'response_format.type' must be 'json_schema' or 'text'"}""", HttpStatusCode.BadRequest)
            : StubAiServer.Chat("""{"title":"Rainy Sunday"}"""));
        var endpoint = Endpoint(server);

        var first = await endpoint.ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Make a playlist", 0.4, TestContext.Current.CancellationToken);
        var second = await endpoint.ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Make another", 0.4, TestContext.Current.CancellationToken);

        first.Content.Should().Be("""{"title":"Rainy Sunday"}""");
        second.Content.Should().Be("""{"title":"Rainy Sunday"}""");
        server.Calls.Should().HaveCount(3);
        server.Calls[1].Has("response_format").Should().BeFalse();
        server.Calls[1].Has("temperature").Should().BeFalse();
        server.Calls[2].Has("response_format").Should().BeFalse();
    }

    [Fact]
    public async Task What_one_model_refuses_is_still_sent_to_another()
    {
        var server = new StubAiServer(call => call.Has("response_format") && call.Body.GetProperty("model").GetString() == "picky"
            ? StubAiServer.Json("{}", HttpStatusCode.BadRequest)
            : StubAiServer.Chat("{}"));
        var endpoint = Endpoint(server);

        await endpoint.ChatAsync(StubAiServer.Ollama(), "picky", null, "One", null, TestContext.Current.CancellationToken);
        await endpoint.ChatAsync(StubAiServer.Ollama(), "easygoing", null, "Two", null, TestContext.Current.CancellationToken);

        server.Calls[^1].Has("response_format").Should().BeTrue();
    }

    [Theory]
    [InlineData("<think>The user wants JSON.</think>\n{\"songs\":[]}", "{\"songs\":[]}")]
    [InlineData("```json\n{\"songs\":[]}\n```", "{\"songs\":[]}")]
    [InlineData("Here you go:\n{\"songs\":[]}\nHope that helps!", "{\"songs\":[]}")]
    [InlineData("  {\"songs\":[]}  ", "{\"songs\":[]}")]
    public async Task The_json_is_lifted_out_of_what_a_chatty_model_wraps_it_in(string answer, string expected)
    {
        var server = new StubAiServer(_ => StubAiServer.Chat(answer));

        var reply = await Endpoint(server).ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Describe these songs", null, TestContext.Current.CancellationToken);

        reply.Content.Should().Be(expected);
    }

    [Fact]
    public async Task An_answer_with_no_choices_has_no_content()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("""{"choices":[]}"""));

        var reply = await Endpoint(server).ChatAsync(StubAiServer.OpenAi(), "gpt-4o-mini", null, "Hello", null, TestContext.Current.CancellationToken);

        reply.Content.Should().BeNull();
    }

    [Fact]
    public async Task OpenAI_refusing_a_request_says_what_it_always_said()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("""{"error":{"message":"Incorrect API key"}}""", HttpStatusCode.Unauthorized));

        var act = () => Endpoint(server).ChatAsync(StubAiServer.OpenAi(), "gpt-4o-mini", null, "Hello", null, TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<AiEndpointException>();
        thrown.Which.Message.Should().Be("OpenAI request failed (401). Check the API key and account billing.");
        thrown.Which.StatusCode.Should().Be(401);
        server.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Another_server_refusing_a_model_names_the_model()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("""{"error":"model not found"}""", HttpStatusCode.NotFound));

        var act = () => Endpoint(server).ChatAsync(StubAiServer.Ollama(), "llama9", null, "Hello", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>())
            .Which.Message.Should().Be("The AI server at ollama.example:11434 has no model named \"llama9\", or the address is wrong (404).");
    }

    [Fact]
    public async Task A_server_on_localhost_that_cannot_be_reached_explains_docker()
    {
        var server = new StubAiServer(_ => throw new HttpRequestException("Connection refused"));
        var connection = StubAiServer.Connection((AiConnection.BaseUrlKey, "http://localhost:11434/v1"));

        var act = () => Endpoint(server).ChatAsync(connection, "llama3.1", null, "Hello", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>())
            .Which.Message.Should().Contain("Couldn't reach the AI server at localhost:11434.").And.Contain("host.docker.internal");
    }

    [Fact]
    public async Task A_server_that_never_answers_is_reported_as_slow_not_as_a_cancelled_task()
    {
        var server = new StubAiServer(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var act = () => Endpoint(server).ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Hello", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>())
            .Which.Message.Should().Be("The AI server at ollama.example:11434 took longer than 15 minutes to answer.");
    }

    [Fact]
    public async Task A_page_that_is_not_json_points_at_the_address()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("<html>Welcome</html>"));

        var act = () => Endpoint(server).ChatAsync(StubAiServer.Ollama(), "llama3.1", null, "Hello", null, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>()).Which.Message.Should().Contain("usually /v1");
    }

    [Fact]
    public async Task Each_vector_goes_to_the_input_its_index_names()
    {
        var server = new StubAiServer(_ => StubAiServer.Embeddings((2, Vector(1536, 3f)), (0, Vector(1536, 1f)), (1, Vector(1536, 2f))));

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.OpenAi(), "text-embedding-3-small", new[] { "First", "Second", "Third" }, TestContext.Current.CancellationToken);

        var call = server.Calls.Should().ContainSingle().Subject;
        call.Url.Should().Be("https://api.openai.com/v1/embeddings");
        call.Body.GetProperty("model").GetString().Should().Be("text-embedding-3-small");
        call.Body.GetProperty("input").GetArrayLength().Should().Be(3);
        call.Has("dimensions").Should().BeFalse();
        reply.Vectors.Select(v => v?[0]).Should().Equal(1f, 2f, 3f);
        reply.Dimensions.Should().Be(1536);
        reply.TotalTokens.Should().Be(10);
    }

    [Fact]
    public async Task A_short_answer_leaves_the_rest_empty()
    {
        var server = new StubAiServer(_ => StubAiServer.Embeddings((0, Vector(1536, 1f)), (1, Vector(1536, 2f)), (7, Vector(1536, 9f))));

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.OpenAi(), "text-embedding-3-small", new[] { "First", "Second", "Third" }, TestContext.Current.CancellationToken);

        reply.Vectors.Select(v => v?[0]).Should().Equal(1f, 2f, null);
    }

    [Fact]
    public async Task A_server_that_sends_no_indexes_is_read_in_order()
    {
        var server = new StubAiServer(_ => StubAiServer.Embeddings((null, Vector(768, 1f)), (null, Vector(768, 2f))));

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.Ollama(), "nomic-embed-text", new[] { "First", "Second" }, TestContext.Current.CancellationToken);

        reply.Vectors.Select(v => v?[0]).Should().Equal(1f, 2f);
    }

    [Fact]
    public async Task A_shorter_vector_is_padded_with_zeros_to_the_stored_length()
    {
        var server = new StubAiServer(_ => StubAiServer.Embeddings((0, Vector(768, 0.25f))));

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.Ollama(), "nomic-embed-text", new[] { "Arrival" }, TestContext.Current.CancellationToken);

        var vector = reply.Vectors.Single();
        vector.Should().NotBeNull();
        vector.Should().HaveCount(AiConnection.StoredDimensions);
        vector.Take(768).Should().OnlyContain(v => v == 0.25f);
        vector.Skip(768).Should().OnlyContain(v => v == 0f);
        reply.Dimensions.Should().Be(768);
    }

    [Fact]
    public void Padding_leaves_how_alike_two_vectors_are_unchanged()
    {
        var a = new[] { 0.2f, 0.9f, -0.4f };
        var b = new[] { 0.7f, 0.1f, 0.3f };

        Cosine(AiEndpoint.Fit(a), AiEndpoint.Fit(b)).Should().BeApproximately(Cosine(a, b), 1e-6);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, lengthA = 0, lengthB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            lengthA += a[i] * a[i];
            lengthB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(lengthA) * Math.Sqrt(lengthB));
    }

    [Fact]
    public async Task A_server_other_than_OpenAI_is_sent_a_library_in_small_batches()
    {
        var server = new StubAiServer(call => StubAiServer.Embeddings(
            Enumerable.Range(0, call.Body.GetProperty("input").GetArrayLength())
                .Select(i => ((int?)i, Vector(8, float.Parse(call.Body.GetProperty("input")[i].GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture))))
                .ToArray()));
        var inputs = Enumerable.Range(0, 70).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.Ollama(), "nomic-embed-text", inputs, TestContext.Current.CancellationToken);

        server.Calls.Select(c => c.Body.GetProperty("input").GetArrayLength()).Should().Equal(32, 32, 6);
        reply.Vectors.Select(v => v?[0]).Should().Equal(Enumerable.Range(0, 70).Select(i => (float?)i));
        reply.TotalTokens.Should().Be(30);
    }

    [Fact]
    public async Task A_model_that_returns_too_many_numbers_is_asked_for_fewer_and_remembered()
    {
        var server = new StubAiServer(call => StubAiServer.Embeddings((0, Vector(call.Has("dimensions") ? 1536 : 3072))));
        var endpoint = Endpoint(server);

        var first = await endpoint.EmbedAsync(StubAiServer.OpenAi(), "text-embedding-3-large", new[] { "Arrival" }, TestContext.Current.CancellationToken);
        await endpoint.EmbedAsync(StubAiServer.OpenAi(), "text-embedding-3-large", new[] { "Dune" }, TestContext.Current.CancellationToken);

        first.Vectors.Single().Should().HaveCount(1536);
        server.Calls.Should().HaveCount(3);
        server.Calls[1].Body.GetProperty("dimensions").GetInt32().Should().Be(1536);
        server.Calls[2].Has("dimensions").Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_model_that_cannot_return_fewer_numbers_is_refused_by_name(bool refusesTheOption)
    {
        var server = new StubAiServer(call => call.Has("dimensions") && refusesTheOption
            ? StubAiServer.Json("""{"error":"unknown field dimensions"}""", HttpStatusCode.BadRequest)
            : StubAiServer.Embeddings((0, Vector(4096))));

        var act = () => Endpoint(server).EmbedAsync(StubAiServer.Ollama(), "qwen3-embedding:8b", new[] { "Arrival" }, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>())
            .Which.Message.Should().Be("The embedding model \"qwen3-embedding:8b\" returns 4,096 numbers per item and Vora stores at most 1,536. Choose a smaller embedding model.");
    }

    [Fact]
    public async Task Nothing_to_embed_sends_nothing()
    {
        var server = new StubAiServer(_ => StubAiServer.Embeddings());

        var reply = await Endpoint(server).EmbedAsync(StubAiServer.OpenAi(), "text-embedding-3-small", Array.Empty<string>(), TestContext.Current.CancellationToken);

        reply.Vectors.Should().BeEmpty();
        server.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_model_list_is_split_into_chat_and_embedding_models()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("nomic-embed-text:latest", "llama3.1:8b", "qwen3:14b", "bge-m3", "LLAMA3.1:8b"));

        var catalog = await Endpoint(server).ListModelsAsync(StubAiServer.Ollama(), TestContext.Current.CancellationToken);

        var call = server.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(HttpMethod.Get);
        call.Url.Should().Be("http://ollama.example:11434/v1/models");
        catalog.ChatModels.Should().Equal("llama3.1:8b", "qwen3:14b");
        catalog.EmbeddingModels.Should().Equal("bge-m3", "nomic-embed-text:latest");
    }

    [Fact]
    public async Task OpenAIs_list_leaves_out_what_cannot_chat()
    {
        var server = new StubAiServer(_ => StubAiServer.Models("gpt-4o-mini", "gpt-5", "whisper-1", "tts-1-hd", "dall-e-3", "gpt-4o-realtime-preview", "text-embedding-3-small", "omni-moderation-latest"));

        var catalog = await Endpoint(server).ListModelsAsync(StubAiServer.OpenAi(), TestContext.Current.CancellationToken);

        catalog.ChatModels.Should().Equal("gpt-4o-mini", "gpt-5");
        catalog.EmbeddingModels.Should().Equal("text-embedding-3-small");
    }

    [Theory]
    [InlineData("""["llama3.1","nomic-embed-text"]""")]
    [InlineData("""{"models":[{"name":"llama3.1"},{"model":"nomic-embed-text"}]}""")]
    public async Task Other_shapes_of_model_list_are_read_too(string json)
    {
        var server = new StubAiServer(_ => StubAiServer.Json(json));

        var catalog = await Endpoint(server).ListModelsAsync(StubAiServer.Ollama(), TestContext.Current.CancellationToken);

        catalog.All.Should().BeEquivalentTo("llama3.1", "nomic-embed-text");
    }

    [Fact]
    public async Task A_refused_model_list_carries_the_status()
    {
        var server = new StubAiServer(_ => StubAiServer.Json("{}", HttpStatusCode.TooManyRequests));

        var act = () => Endpoint(server).ListModelsAsync(StubAiServer.OpenAi(), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiEndpointException>()).Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public void A_document_that_is_no_list_has_no_models()
    {
        AiModelNames.Read(JsonDocument.Parse("""{"object":"error"}""").RootElement).Should().BeEmpty();
    }
}
