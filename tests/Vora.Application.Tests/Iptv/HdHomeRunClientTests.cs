using Vora.Application.Iptv;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class HdHomeRunClientTests
{
    private const string Status = """[{"Resource":"tuner0","VctNumber":"7.1","VctName":"WABC-DT","TargetIP":"192.168.1.20"},{"Resource":"tuner1"}]""";

    private readonly StubUpstream _upstream = new();

    private HdHomeRunClient Client() => new(_upstream);

    private static IptvPlaylist Tuner(string? address = HdHomeRunFixtures.Address) => new()
    {
        Name = "Antenna",
        SourceType = IptvSourceType.HdHomeRun,
        ServerUrl = address
    };

    [Fact]
    public async Task The_status_is_the_device_and_its_tuners()
    {
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover)
            .On(uri => uri.AbsolutePath == "/status.json", Status);

        var status = await Client().GetStatusAsync(Tuner(), TestContext.Current.CancellationToken);

        status.Device.Should().Be(new HdHomeRunDevice("HDHomeRun FLEX 4K", "HDHR5-4K", "1080ABCD", 4, "20240101"));
        status.Tuners.Select(t => t.InUse).Should().Equal(true, false);
        _upstream.Requests.Select(r => r.RequestUri!.ToString()).Should().Equal("http://192.168.1.50/discover.json", "http://192.168.1.50/status.json");
    }

    [Fact]
    public async Task A_device_whose_discover_page_answers_an_error_still_shows_its_tuners()
    {
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", "no", status: System.Net.HttpStatusCode.NotFound)
            .On(uri => uri.AbsolutePath == "/status.json", Status);

        var status = await Client().GetStatusAsync(Tuner(), TestContext.Current.CancellationToken);

        status.Device.Should().BeNull();
        status.Tuners.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_device_that_cannot_be_reached_is_asked_once()
    {
        _upstream.Fail(_ => true, new HttpRequestException("No route to host"));

        var read = () => Client().GetStatusAsync(Tuner(), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<IptvSourceException>().WithMessage("The HDHomeRun could not be reached: No route to host");
        _upstream.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_status_page_that_answers_an_error_is_reported_with_the_code()
    {
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover)
            .On(uri => uri.AbsolutePath == "/status.json", "busy", status: System.Net.HttpStatusCode.ServiceUnavailable);

        var read = () => Client().GetStatusAsync(Tuner(), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<IptvSourceException>().WithMessage("The HDHomeRun answered HTTP 503.");
    }

    [Fact]
    public async Task The_device_authorization_is_read_from_its_discover_page()
    {
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", HdHomeRunFixtures.Discover);

        var auth = await Client().GetDeviceAuthAsync(Tuner(), TestContext.Current.CancellationToken);

        auth.Should().Be("secret-auth");
    }

    [Fact]
    public async Task A_device_without_an_authorization_says_so()
    {
        _upstream.On(uri => uri.AbsolutePath == "/discover.json", """{"FriendlyName":"Old"}""");

        var read = () => Client().GetDeviceAuthAsync(Tuner(), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<IptvSourceException>().WithMessage("*no authorization*");
    }

    [Fact]
    public async Task A_tuner_with_no_address_cannot_be_asked()
    {
        var read = () => Client().GetStatusAsync(Tuner(address: null), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<IptvSourceException>().WithMessage("*needs its address*");
        _upstream.Requests.Should().BeEmpty();
    }
}
