using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Ai.ViewModels;
using Vora.Application.Plugins.ViewModels;

namespace Vora.Api.Tests;

public class AiSettingsEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private const string PluginId = "openai_recommendations";

    private readonly VoraApiTestFactory _factory;

    public AiSettingsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(bool isAdmin)
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: isAdmin);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // One test, in this order: the tests of a class share a database, so a
    // separate "defaults" test would see whatever the other had saved.
    [Fact]
    public async Task A_server_that_saved_nothing_is_on_the_OpenAI_defaults_and_status_follows_what_is_saved_after()
    {
        var client = Client(isAdmin: true);

        var fields = await client.GetFromJsonAsync<List<PluginSettingFieldVM>>($"/api/settings/plugins/{PluginId}", TestContext.Current.CancellationToken);
        var before = await client.GetFromJsonAsync<AiStatusVM>("/api/settings/ai/status", TestContext.Current.CancellationToken);
        var put = await client.PutAsJsonAsync($"/api/settings/plugins/{PluginId}", new Dictionary<string, string>
        {
            ["base_url"] = "http://ollama.example:11434",
            ["chat_model"] = "llama3.1",
            ["embedding_model"] = ""
        }, TestContext.Current.CancellationToken);
        var after = await client.GetFromJsonAsync<AiStatusVM>("/api/settings/ai/status", TestContext.Current.CancellationToken);

        var values = (fields ?? new List<PluginSettingFieldVM>()).ToDictionary(f => f.Key, f => f.Value);
        values["base_url"].Should().Be("https://api.openai.com/v1");
        values["chat_model"].Should().Be("gpt-4o-mini");
        values["collections_model"].Should().Be("gpt-5");
        values["music_playlists_model"].Should().BeEmpty();
        values["embedding_model"].Should().Be("text-embedding-3-small");

        (before?.Configured).Should().BeFalse();
        (before?.IsOpenAi).Should().BeTrue();
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (after?.Configured).Should().BeTrue();
        (after?.Embeddings).Should().BeFalse();
        (after?.IsOpenAi).Should().BeFalse();
        (after?.Host).Should().Be("ollama.example:11434");
    }

    [Fact]
    public async Task Listing_models_without_a_key_for_OpenAI_asks_for_one_and_calls_nobody()
    {
        var response = await Client(isAdmin: true).PostAsJsonAsync("/api/settings/ai/models", new Dictionary<string, string>(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var models = await response.Content.ReadFromJsonAsync<AiModelsVM>(TestContext.Current.CancellationToken);
        (models?.Error).Should().Be("Enter an API key first.");
        (models?.ChatModels).Should().BeEmpty();
    }

    [Theory]
    [InlineData("GET", "/api/settings/ai/status")]
    [InlineData("POST", "/api/settings/ai/models")]
    public async Task Only_an_admin_reaches_the_ai_settings(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new Dictionary<string, string>());

        var response = await Client(isAdmin: false).SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
