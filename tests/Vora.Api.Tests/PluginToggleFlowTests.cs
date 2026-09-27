using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Plugins.ViewModels;

namespace Vora.Api.Tests;

public class PluginToggleFlowTests : IClassFixture<VoraApiTestFactory>
{
    // A stable built-in system plugin used as the subject for the generic
    // plugin-toggle flow (listing, enable/disable, admin-only auth).
    private const string PluginId = "tmdb_metadata";
    private const string PluginName = "The Movie Database (TMDB)";

    private readonly VoraApiTestFactory _factory;

    public PluginToggleFlowTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GET_plugins_returns_builtin_plugin_in_the_listing()
    {
        // NOTE: this class uses IClassFixture so the in-memory DB is shared across tests
        // and xUnit doesn't guarantee execution order. We can't reliably assert the
        // default IsEnabled state here because the toggle test may have already run.
        var client = AdminClient();

        var response = await client.GetAsync("/api/plugins", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plugins = await response.Content.ReadFromJsonAsync<List<PluginVM>>(TestContext.Current.CancellationToken);
        plugins.Should().NotBeNull();
        var yt = plugins!.FirstOrDefault(p => p.Id == PluginId);
        yt.Should().NotBeNull($"{PluginId} is a built-in plugin and should always be present");
        yt!.Name.Should().Be(PluginName);
        yt.IsSystemPlugin.Should().BeTrue();
    }

    [Fact]
    public async Task Toggling_is_enabled_false_then_GET_reflects_disabled_state()
    {
        var client = AdminClient();

        // Toggle off
        var put = await client.PutAsJsonAsync($"/api/settings/plugins/{PluginId}", new Dictionary<string, string>
        {
            ["is_enabled"] = "false"
        },
        TestContext.Current.CancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify via plugin listing
        var get = await client.GetAsync("/api/plugins", TestContext.Current.CancellationToken);
        var plugins = await get.Content.ReadFromJsonAsync<List<PluginVM>>(TestContext.Current.CancellationToken);
        var yt = plugins!.Single(p => p.Id == PluginId);
        yt.IsEnabled.Should().BeFalse();

        // Verify via per-plugin settings endpoint
        var settings = await client.GetFromJsonAsync<List<PluginSettingFieldVM>>($"/api/settings/plugins/{PluginId}", TestContext.Current.CancellationToken);
        var enabledField = settings!.Single(f => f.Key == "is_enabled");
        enabledField.Value.Should().Be("false");
    }

    [Fact]
    public async Task Toggling_back_to_true_restores_enabled_state()
    {
        var client = AdminClient();

        await client.PutAsJsonAsync($"/api/settings/plugins/{PluginId}", new Dictionary<string, string>
        {
            ["is_enabled"] = "false"
        },
        TestContext.Current.CancellationToken);
        await client.PutAsJsonAsync($"/api/settings/plugins/{PluginId}", new Dictionary<string, string>
        {
            ["is_enabled"] = "true"
        },
        TestContext.Current.CancellationToken);

        var get = await client.GetAsync("/api/plugins", TestContext.Current.CancellationToken);
        var plugins = await get.Content.ReadFromJsonAsync<List<PluginVM>>(TestContext.Current.CancellationToken);
        var yt = plugins!.Single(p => p.Id == PluginId);
        yt.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Non_admin_user_cannot_read_plugin_listing()
    {
        var profileToken = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profileToken);

        var response = await client.GetAsync("/api/plugins", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Non_admin_user_cannot_toggle_plugin_settings()
    {
        var profileToken = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", profileToken);

        var response = await client.PutAsJsonAsync($"/api/settings/plugins/{PluginId}", new Dictionary<string, string>
        {
            ["is_enabled"] = "false"
        },
        TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
