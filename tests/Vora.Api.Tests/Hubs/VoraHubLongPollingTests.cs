using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Hubs;
using Vora.Api.Tests.Infra;

namespace Vora.Api.Tests.Hubs;

public class VoraHubLongPollingTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public VoraHubLongPollingTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    // The Roku has no WebSocket and long-polls the hub. SignalR holds a poll up
    // to 90 s by default, and an nginx proxy (Nginx Proxy Manager) gives up on
    // an upstream read after 60 s, so every idle poll ended in a 504 and a
    // reconnect. A poll that ends at 45 s answers an empty 200 first.
    [Fact]
    public void The_hub_ends_an_idle_long_poll_before_a_proxys_sixty_second_timeout()
    {
        _factory.CreateClient();
        var hubEndpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/hubs/Vora", StringComparison.Ordinal) == true)
            .ToList();

        var connectionOptions = hubEndpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<HttpConnectionDispatcherOptions>())
            .OfType<HttpConnectionDispatcherOptions>()
            .ToList();

        connectionOptions.Should().NotBeEmpty();
        connectionOptions.Select(options => options.LongPolling.PollTimeout).Should().AllBeEquivalentTo(TimeSpan.FromSeconds(45));
        VoraHubConnections.LongPollTimeout.Should().BeLessThan(TimeSpan.FromSeconds(60));
    }
}
