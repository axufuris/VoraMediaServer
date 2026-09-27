using System.Net;
using System.Net.Http.Headers;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

// The subtitle route lives on a complex path segment ("{subtitleTrackId}.vtt"),
// so "does it even match" is a real question — a mis-declared template shows up
// as a 404 that looks exactly like a missing session. These pin that the route
// exists, is authenticated like the rest of /api/streaming, and answers 404
// rather than 500 for everything it can't serve.
public class SubtitleEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public SubtitleEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid sessionId, Guid trackId) =>
        $"/api/streaming/sessions/{sessionId}/subtitle/{trackId}.vtt";

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));
        return client;
    }

    [Fact]
    public async Task An_anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // 404 rather than 401 proves the route matched and the handler ran: the
    // template's ".vtt" literal suffix is parsed the way it reads.
    [Fact]
    public async Task An_unknown_session_is_a_404()
    {
        var response = await AuthenticatedClient().GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The track id is parsed in the handler rather than constrained in the
    // template, so a non-guid has to fall out as 404 and not as an unhandled
    // format exception.
    [Fact]
    public async Task A_track_id_that_is_not_a_guid_is_a_404()
    {
        var response = await AuthenticatedClient().GetAsync(
            $"/api/streaming/sessions/{Guid.NewGuid()}/subtitle/not-a-guid.vtt", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_session_id_that_is_not_a_guid_does_not_match_the_route()
    {
        var response = await AuthenticatedClient().GetAsync(
            $"/api/streaming/sessions/nope/subtitle/{Guid.NewGuid()}.vtt", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Without the extension the request is a different resource entirely; it
    // must not fall through to some other streaming route.
    [Fact]
    public async Task The_extension_is_part_of_the_route()
    {
        var response = await AuthenticatedClient().GetAsync(
            $"/api/streaming/sessions/{Guid.NewGuid()}/subtitle/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
