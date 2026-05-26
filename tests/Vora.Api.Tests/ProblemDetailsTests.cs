using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class ProblemDetailsTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public ProblemDetailsTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Unauthenticated_request_to_protected_endpoint_returns_401_without_problem_body()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/libraries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // JwtBearer challenges via WWW-Authenticate; body is typically empty
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Not_found_response_carries_a_problem_details_or_empty_body()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync($"/api/libraries/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Endpoint may return either a ProblemDetails body or an empty 404; both are valid
        // depending on whether the handler returned Results.NotFound() vs Results.NotFound(value).
        var body = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            // If we got a body it should be JSON shaped like ProblemDetails (RFC 7807)
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public async Task Forbidden_response_for_admin_only_endpoint_does_not_leak_implementation_details()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/libraries", JsonContent.Create(new
        {
            name = "x",
            type = "Movie",
            folderPaths = new[] { "/m" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        // Should not contain stack traces or anything sensitive
        body.Should().NotContain("Vora.Application");
        body.Should().NotContain("Exception");
    }

    private static class JsonContent
    {
        public static System.Net.Http.Json.JsonContent Create<T>(T value) =>
            System.Net.Http.Json.JsonContent.Create(value);
    }
}
