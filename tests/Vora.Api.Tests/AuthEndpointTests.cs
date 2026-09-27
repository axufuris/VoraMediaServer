using System.Net;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class AuthEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public AuthEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_setup_status_returns_200_with_expected_shape_on_empty_database()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/setup-status", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SetupStatusResponse>(TestContext.Current.CancellationToken);
        payload.Should().NotBeNull();
        payload!.IsClaimed.Should().BeFalse();
    }

    [Fact]
    public async Task POST_login_returns_400_or_401_for_missing_credentials()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_libraries_returns_401_without_auth_token()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/libraries", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class SetupStatusResponse
    {
        public bool IsClaimed { get; set; }
        public int RegistrationMode { get; set; }
        public string? ServerName { get; set; }
        public bool EmailEnabled { get; set; }
    }
}
