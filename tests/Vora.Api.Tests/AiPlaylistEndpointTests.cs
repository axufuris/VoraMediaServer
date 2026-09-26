using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Media.Ai;

namespace Vora.Api.Tests;

public class AiPlaylistEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public AiPlaylistEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(bool isAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: isAdmin));
        return client;
    }

    // Forcing a run spends the server's OpenAI budget for every profile.
    [Fact]
    public async Task Only_an_admin_can_force_AI_playlists_to_be_made()
    {
        var response = await Client(isAdmin: false).PostAsync("/api/music/ai/generate", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Off by default: a server that never turned it on just says so.
    [Fact]
    public async Task A_server_that_never_turned_it_on_reports_it_off()
    {
        var response = await Client(isAdmin: false).GetAsync("/api/music/ai", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AiPlaylistsVM>(TestContext.Current.CancellationToken);
        body!.Enabled.Should().BeFalse();
    }
}
