using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class ProfileViewingHistoryEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public ProfileViewingHistoryEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NonAdmin(Guid accountId, Guid profileId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(accountId, profileId, isAdmin: false));
        return client;
    }

    [Fact]
    public async Task A_non_admin_cannot_see_a_profiles_history_counts_even_its_own()
    {
        var profileId = Guid.NewGuid();
        var response = await NonAdmin(Guid.NewGuid(), profileId).GetAsync(
            $"/api/users/profiles/{profileId}/viewing-history", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_non_admin_cannot_clear_a_profiles_history_even_its_own()
    {
        var profileId = Guid.NewGuid();
        var response = await NonAdmin(Guid.NewGuid(), profileId).PostAsJsonAsync(
            $"/api/users/profiles/{profileId}/viewing-history/clear",
            new { watchHistory = true, ratings = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
