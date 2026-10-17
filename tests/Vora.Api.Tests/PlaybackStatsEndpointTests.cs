using System.Net;
using System.Net.Http.Headers;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class PlaybackStatsEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public PlaybackStatsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid sessionId) => $"/api/streaming/sessions/{sessionId}/stats";

    [Fact]
    public async Task An_anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync(Url(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_stream_that_does_not_exist_is_not_found()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));

        var response = await client.GetAsync(Url(Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
