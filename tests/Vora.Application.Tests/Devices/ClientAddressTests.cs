using Vora.Application.Devices;

namespace Vora.Application.Tests.Devices;

public class ClientAddressTests
{
    // Docker's networks are 172.16.0.0/12. The old check only knew 192.168,
    // 10 and 127, so a server in Docker sent its gateway out to be looked up.
    [Theory]
    [InlineData("172.20.0.1")]
    [InlineData("172.16.5.4")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.20")]
    [InlineData("10.0.0.5")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.10.1")]
    [InlineData("100.101.102.103")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("::ffff:172.20.0.1")]
    public void Private_and_reserved_addresses_are_local(string ip)
    {
        ClientAddress.IsLocal(ip).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("Unknown IP")]
    public void Public_or_unreadable_addresses_are_not_local(string ip)
    {
        ClientAddress.IsLocal(ip).Should().BeFalse();
    }

    // The reply shape captured from ipwho.is.
    [Fact]
    public void A_successful_reply_reads_as_city_region_country()
    {
        ClientAddress.ParseLocation("""{"success":true,"country_code":"US","region":"California","region_code":"CA","city":"San Jose"}""")
            .Should().Be("San Jose, CA, US");
    }

    [Theory]
    [InlineData("""{"ip":"172.20.0.1","success":false,"message":"Reserved range"}""")]
    [InlineData("not json")]
    [InlineData("""{"success":true}""")]
    public void A_failed_or_empty_reply_has_no_location(string json)
    {
        ClientAddress.ParseLocation(json).Should().BeNull();
    }

    [Fact]
    public void The_lookup_is_over_https()
    {
        ClientAddress.LookupUrl("8.8.8.8").Should().StartWith("https://");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Unknown Location", true)]
    [InlineData("Local Network", false)]
    [InlineData("San Jose, CA, US", false)]
    public void Devices_without_a_location_are_looked_up_again(string? location, bool expected)
    {
        ClientAddress.NeedsLookup(location).Should().Be(expected);
    }
}
