using System.Net;
using Vora.Application.Devices;

namespace Vora.Application.Tests.Devices;

public class ClientLocationResolverTests
{
    private const string PublicAddress = "203.0.113.9";
    private const string Denver = """{"success":true,"city":"Denver","region":"Colorado","region_code":"CO","country_code":"US"}""";

    private sealed class Lookups : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Denver) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Answer());
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Lookups _lookups = new();
    private readonly Clock _clock = new();

    private ClientLocationResolver Resolver()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(ClientLocationResolver.HttpClientName).Returns(_ => new HttpClient(_lookups, disposeHandler: false));
        return new ClientLocationResolver(factory, _clock);
    }

    [Theory]
    [InlineData("192.168.1.20", ClientAddress.LocalNetwork)]
    [InlineData("172.18.0.1", ClientAddress.LocalNetwork)]
    [InlineData("100.101.102.103", ClientAddress.Vpn)]
    public async Task An_address_on_the_home_network_or_a_vpn_is_never_sent_to_the_lookup(string address, string expected)
    {
        (await Resolver().ResolveAsync(address)).Should().Be(expected);

        _lookups.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_public_address_is_looked_up()
    {
        (await Resolver().ResolveAsync(PublicAddress)).Should().Be(ClientAddress.ParseLocation(Denver));
    }

    [Fact]
    public async Task The_same_address_is_looked_up_once_then_remembered_for_half_a_day()
    {
        var resolver = Resolver();

        await resolver.ResolveAsync(PublicAddress);
        await resolver.ResolveAsync(PublicAddress);
        _lookups.Calls.Should().Be(1);

        _clock.Now += TimeSpan.FromHours(13);
        await resolver.ResolveAsync(PublicAddress);
        _lookups.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_lookup_keeps_the_location_it_was_given_and_is_tried_again_next_time()
    {
        _lookups.Answer = () => throw new HttpRequestException("no route");
        var resolver = Resolver();

        (await resolver.ResolveAsync(PublicAddress, "Denver, CO, US")).Should().Be("Denver, CO, US");
        (await resolver.ResolveAsync(PublicAddress)).Should().Be(ClientAddress.UnknownLocation);

        _lookups.Calls.Should().Be(2);
    }
}
