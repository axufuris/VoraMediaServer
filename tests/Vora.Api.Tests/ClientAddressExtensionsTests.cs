using System.Net;
using Microsoft.AspNetCore.Http;
using Vora.Api.Extensions;

namespace Vora.Api.Tests;

public class ClientAddressExtensionsTests
{
    private static HttpContext From(IPAddress? address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        return context;
    }

    [Fact]
    public void An_ipv4_address_is_returned_as_written()
    {
        From(IPAddress.Parse("203.0.113.9")).GetClientAddress().Should().Be("203.0.113.9");
    }

    [Fact]
    public void An_ipv4_address_wrapped_in_ipv6_is_unwrapped()
    {
        From(IPAddress.Parse("::ffff:192.168.1.20")).GetClientAddress().Should().Be("192.168.1.20");
    }

    [Fact]
    public void An_ipv6_address_is_kept()
    {
        From(IPAddress.Parse("2001:db8::1")).GetClientAddress().Should().Be("2001:db8::1");
    }

    [Fact]
    public void A_request_without_an_address_has_none()
    {
        From(null).GetClientAddress().Should().BeNull();
    }
}
