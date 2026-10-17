using System.Net;
using System.Net.Http.Headers;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class BandwidthSampleEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public BandwidthSampleEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task An_anonymous_request_is_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/streaming/bandwidth-sample?kb=512", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_client_gets_the_size_it_asked_for_and_nothing_cached()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));

        var response = await client.GetAsync("/api/streaming/bandwidth-sample?kb=512", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Length.Should().Be(512 * 1024);
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
    }
}
