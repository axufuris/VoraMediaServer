using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class CollectionArtworkEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public CollectionArtworkEndpointTests(VoraApiTestFactory factory)
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

    private static StringContent JsonString(string value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    [Fact]
    public async Task Finding_artwork_for_a_new_collection_answers_a_list()
    {
        using var client = Client(isAdmin: true);

        using var response = await client.GetAsync("/api/collections/artwork/search?providerId=no_such_provider&title=Star%20Wars", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonDocument.Parse(body).RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Only_admins_find_or_set_collection_artwork()
    {
        using var client = Client(isAdmin: false);

        using var search = await client.GetAsync("/api/collections/artwork/search?providerId=tmdb_artwork&title=Star%20Wars", TestContext.Current.CancellationToken);
        using var select = await client.PutAsync($"/api/collections/{Guid.NewGuid()}/artwork/selected?kind=Poster", JsonString("/api/artwork/custom/poster.jpg"), TestContext.Current.CancellationToken);

        search.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        select.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Setting_the_poster_of_a_missing_collection_is_refused()
    {
        using var client = Client(isAdmin: true);

        using var response = await client.PutAsync($"/api/collections/{Guid.NewGuid()}/artwork/selected?kind=Poster", JsonString("/api/artwork/custom/poster.jpg"), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
