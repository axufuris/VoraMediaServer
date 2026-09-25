using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

// Creating accounts directly is an admin's power only: a profile that could do
// it could hand itself a second, unrestricted account.
public class AdminCreateUserEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public AdminCreateUserEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_non_admin_cannot_create_an_account()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false));

        var response = await client.PostAsJsonAsync("/api/users",
            new { email = "sam@example.com", displayName = "Sam", password = "a-long-password" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
