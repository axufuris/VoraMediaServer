using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Api.Extensions;
using Vora.Application.Settings;

namespace Vora.Api.Tests;

// With no configuration, a reverse proxy on the same machine, in Docker beside
// Vora, or anywhere on the LAN passes the real client address through - and
// nothing on the internet can use the header to pretend to be someone else.
public class ForwardedHeadersDefaultsTests
{
    private static async Task<string?> ClientAddressAsync(string peer, string? forwardedFor, ForwardedHeadersConfigOptions? config = null)
    {
        var options = WebApplicationExtensions.BuildForwardedHeadersOptions(config ?? new ForwardedHeadersConfigOptions());
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (forwardedFor != null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        await middleware.Invoke(context);
        return context.Connection.RemoteIpAddress?.ToString();
    }

    [Fact]
    public void It_is_on_out_of_the_box()
    {
        new ForwardedHeadersConfigOptions().Enabled.Should().BeTrue();
    }

    // The screenshot: every device arrived as Docker's gateway.
    [Theory]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.2")]
    [InlineData("127.0.0.1")]
    public async Task A_proxy_on_a_private_network_passes_the_real_client_through(string proxy)
    {
        (await ClientAddressAsync(proxy, "203.0.113.9")).Should().Be("203.0.113.9");
    }

    [Fact]
    public async Task A_chain_of_private_proxies_is_walked_back_to_the_client()
    {
        (await ClientAddressAsync("172.20.0.5", "203.0.113.9, 10.0.0.7")).Should().Be("203.0.113.9");
    }

    // A client on the internet adds its own "I'm on your LAN" entry; the proxy
    // appends the real address after it. Reading right to left stops at the
    // first public address, so the fake one is never reached.
    [Fact]
    public async Task A_client_cannot_spoof_its_address_through_the_proxy()
    {
        (await ClientAddressAsync("172.20.0.1", "192.168.1.5, 203.0.113.9")).Should().Be("203.0.113.9");
    }

    // Nothing on the internet is trusted to forward.
    [Fact]
    public async Task A_public_peer_cannot_claim_a_different_address()
    {
        (await ClientAddressAsync("198.51.100.4", "192.168.1.5")).Should().Be("198.51.100.4");
    }

    [Fact]
    public async Task Without_a_forwarding_header_the_peer_is_the_client()
    {
        (await ClientAddressAsync("192.168.1.20", null)).Should().Be("192.168.1.20");
    }

    // An admin who lists their proxy gets exactly that and nothing wider.
    [Fact]
    public async Task Configured_proxies_replace_the_private_network_default()
    {
        var config = new ForwardedHeadersConfigOptions { KnownProxies = new List<string> { "10.0.0.2" } };

        (await ClientAddressAsync("10.0.0.2", "203.0.113.9", config)).Should().Be("203.0.113.9");
        (await ClientAddressAsync("172.20.0.1", "203.0.113.9", config)).Should().Be("172.20.0.1");
    }
}
