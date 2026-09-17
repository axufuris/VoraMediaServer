using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class MediaMatchEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public MediaMatchEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(bool isAdmin)
    {
        var client = _factory.CreateClient();
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: isAdmin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Searching_for_a_match_is_admin_only()
    {
        using var client = Client(isAdmin: false);

        using var response = await client.GetAsync($"/api/media/{Guid.NewGuid()}/match/candidates?query=Dead%20City", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Applying_a_match_is_admin_only()
    {
        using var client = Client(isAdmin: false);

        using var response = await client.PostAsJsonAsync($"/api/media/{Guid.NewGuid()}/match", new { source = "tvdb", externalId = "417549" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("trakt", "417549")]
    [InlineData("imdb", "not-an-imdb-id")]
    public async Task An_invalid_match_is_a_bad_request(string source, string externalId)
    {
        using var client = Client(isAdmin: true);

        using var response = await client.PostAsJsonAsync($"/api/media/{Guid.NewGuid()}/match", new { source, externalId }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Matching_an_item_that_does_not_exist_is_not_found()
    {
        using var client = Client(isAdmin: true);

        using var response = await client.GetAsync($"/api/media/{Guid.NewGuid()}/match/candidates", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
