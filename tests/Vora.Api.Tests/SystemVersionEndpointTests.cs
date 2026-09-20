using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vora.Api.Tests.Infra;
using Vora.Application.Settings.ViewModels;

namespace Vora.Api.Tests;

public class SystemVersionEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public SystemVersionEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_version_is_not_handed_out_anonymously()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/system/version", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_profile_gets_the_running_version()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));

        var version = await client.GetFromJsonAsync<ServerVersionVM>("/api/system/version", TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The version endpoint returned no body.");

        version.Version.Should().NotBeNullOrWhiteSpace();
        version.Version.Should().NotBe("unknown");
    }
}
