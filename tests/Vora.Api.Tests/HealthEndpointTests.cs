using System.Net;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests;

public class HealthEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public HealthEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GET_health_returns_200_with_status_body()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();
        body.Should().ContainAny("Healthy", "Degraded");
    }
}
