using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class CollectionHiddenEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public CollectionHiddenEndpointTests(VoraApiTestFactory factory)
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

    private static StringContent Body(bool hidden) =>
        new($"{{\"hidden\":{(hidden ? "true" : "false")}}}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task A_viewer_cannot_hide_a_collection()
    {
        using var client = Client(isAdmin: false);

        using var response = await client.PutAsync($"/api/collections/{Guid.NewGuid()}/hidden", Body(true), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hiding_a_collection_that_is_gone_is_not_found()
    {
        using var client = Client(isAdmin: true);

        using var response = await client.PutAsync($"/api/collections/{Guid.NewGuid()}/hidden", Body(true), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
