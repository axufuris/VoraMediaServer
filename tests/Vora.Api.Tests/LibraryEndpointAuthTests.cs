using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class LibraryEndpointAuthTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public LibraryEndpointAuthTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task GET_libraries_returns_200_with_valid_profile_token()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid());
        var client = ClientWithToken(token);

        var response = await client.GetAsync("/api/libraries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_libraries_returns_401_when_token_signed_with_wrong_secret()
    {
        // Issue a token with a different secret to prove signature verification works.
        var bad = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: JwtTestHelpers.Issuer,
            audience: JwtTestHelpers.Audience,
            claims: new[] {
                new System.Security.Claims.Claim("accountId", Guid.NewGuid().ToString()),
                new System.Security.Claims.Claim("stamp", "x"),
            },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("a-different-secret-also-32-bytes-long-aaaa")),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(bad);

        var client = ClientWithToken(token);

        var response = await client.GetAsync("/api/libraries");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_libraries_returns_403_with_non_admin_token()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);
        var client = ClientWithToken(token);

        var response = await client.PostAsJsonAsync("/api/libraries", new
        {
            name = "Test Movies",
            type = "Movie",
            folderPaths = new[] { "/media/movies" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task POST_libraries_returns_201_with_admin_token()
    {
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true);
        var client = ClientWithToken(token);

        var response = await client.PostAsJsonAsync("/api/libraries", new
        {
            name = "Test Movies " + Guid.NewGuid().ToString("N").Substring(0, 8),
            type = "Movie",
            folderPaths = new[] { "/media/movies" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("id", "the create endpoint returns the new library id");
    }

    [Fact]
    public async Task POST_libraries_returns_401_without_any_token()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/libraries", new
        {
            name = "Anonymous",
            type = "Movie",
            folderPaths = new[] { "/media/movies" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
