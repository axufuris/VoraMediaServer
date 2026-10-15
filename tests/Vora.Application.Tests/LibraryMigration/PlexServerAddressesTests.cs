using Vora.Plugins.Dtos;
using Vora.Plugins.Providers.Plex;

namespace Vora.Application.Tests.LibraryMigration;

public class PlexServerAddressesTests
{
    private static readonly RemoteConnectionDto Relay = new() { Uri = "https://203-0-113-50.abc123.plex.direct:8443", Address = "203.0.113.50", Port = 8443, IsRelay = true, IsHttps = true };
    private static readonly RemoteConnectionDto Remote = new() { Uri = "https://198-51-100-7.abc123.plex.direct:32400", Address = "198.51.100.7", Port = 32400, IsHttps = true };
    private static readonly RemoteConnectionDto Local = new() { Uri = "https://192-168-0-10.abc123.plex.direct:32400/", Address = "192.168.0.10", Port = 32400, IsLocal = true, IsHttps = true };

    [Fact]
    public void Local_addresses_come_first_then_remote_then_the_relay_whatever_order_Plex_lists_them_in()
    {
        var addresses = PlexServerAddresses.InTryOrder([Relay, Remote, Local]);

        addresses.Select(a => a.Uri).Should().Equal(
            "https://192-168-0-10.abc123.plex.direct:32400",
            "http://192.168.0.10:32400",
            "https://198-51-100-7.abc123.plex.direct:32400",
            "https://203-0-113-50.abc123.plex.direct:8443");
        addresses.Select(a => a.Label).Should().Equal(
            "the local address 192.168.0.10:32400",
            "the local address 192.168.0.10:32400 over plain HTTP",
            "the remote address 198.51.100.7:32400",
            "the Plex relay");
    }

    [Fact]
    public void A_local_address_is_also_tried_by_its_IP_so_a_blocked_plex_direct_name_is_not_the_end()
    {
        var addresses = PlexServerAddresses.InTryOrder([Local]);

        addresses.Should().Contain(a => a.Uri == "http://192.168.0.10:32400");
    }

    [Fact]
    public void Plain_HTTP_is_never_tried_over_the_internet()
    {
        var addresses = PlexServerAddresses.InTryOrder([Remote, Relay]);

        addresses.Should().OnlyContain(a => a.Uri.StartsWith("https://"));
    }

    [Fact]
    public void An_address_Plex_already_lists_over_HTTP_is_tried_once()
    {
        var plain = new RemoteConnectionDto { Uri = "http://192.168.0.10:32400", Address = "192.168.0.10", Port = 32400, IsLocal = true };

        var addresses = PlexServerAddresses.InTryOrder([Local, plain]);

        addresses.Select(a => a.Uri).Should().Equal("https://192-168-0-10.abc123.plex.direct:32400", "http://192.168.0.10:32400");
    }

    [Fact]
    public void An_IPv6_address_is_named_with_brackets_and_not_tried_over_plain_HTTP()
    {
        var ipv6 = new RemoteConnectionDto { Uri = "https://2001-db8--10.abc123.plex.direct:32400", Address = "2001:db8::10", Port = 32400, IsLocal = true, IsHttps = true };

        var addresses = PlexServerAddresses.InTryOrder([ipv6]);

        addresses.Should().ContainSingle().Which.Label.Should().Be("the local address [2001:db8::10]:32400");
    }

    [Fact]
    public void A_connection_without_an_address_is_named_by_its_host()
    {
        var bare = new RemoteConnectionDto { Uri = "https://plex.example.com:32400", IsHttps = true };

        PlexServerAddresses.InTryOrder([bare]).Should().ContainSingle().Which.Label.Should().Be("the remote address plex.example.com:32400");
    }
}
