using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class ArtworkUrlEndpointContractTests : IClassFixture<VoraApiTestFactory>
{
    private const string UnreachableImageUrl = "http://127.0.0.1/poster.jpg";

    private readonly VoraApiTestFactory _factory;

    public ArtworkUrlEndpointContractTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent JsonString(string value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    public static TheoryData<string> ArtworkUrlRoutes() => new()
    {
        $"/api/collections/{Guid.NewGuid()}/artwork/url",
        $"/api/media/{Guid.NewGuid()}/artwork/url",
    };

    [Theory]
    [MemberData(nameof(ArtworkUrlRoutes))]
    public async Task The_artwork_kind_is_bound_from_kind(string route)
    {
        using var client = AdminClient();

        using var response = await client.PostAsync($"{route}?kind=Poster", JsonString(UnreachableImageUrl), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response)).Should().Be("Invalid operation");
    }

    [Theory]
    [MemberData(nameof(ArtworkUrlRoutes))]
    public async Task A_type_parameter_is_rejected_before_the_handler_runs(string route)
    {
        using var client = AdminClient();

        using var response = await client.PostAsync($"{route}?type=Poster", JsonString(UnreachableImageUrl), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response)).Should().NotBe("Invalid operation");
    }

    [Theory]
    [InlineData("Poster")]
    [InlineData("Backdrop")]
    public async Task Both_artwork_kinds_reach_the_handler(string kind)
    {
        using var client = AdminClient();

        using var response = await client.PostAsync($"/api/collections/{Guid.NewGuid()}/artwork/url?kind={kind}", JsonString(UnreachableImageUrl), TestContext.Current.CancellationToken);

        (await ProblemTitleAsync(response)).Should().Be("Invalid operation");
    }

    [Fact]
    public async Task A_url_containing_a_quote_survives_json_encoding()
    {
        using var client = AdminClient();

        using var response = await client.PostAsync(
            $"/api/collections/{Guid.NewGuid()}/artwork/url?kind=Poster",
            JsonString("http://127.0.0.1/poster \"final\".jpg"),
            TestContext.Current.CancellationToken);

        (await ProblemTitleAsync(response)).Should().Be("Invalid operation");
    }
}
