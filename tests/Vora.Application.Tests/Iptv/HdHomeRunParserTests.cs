using Vora.Application.Iptv;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Iptv;

public class HdHomeRunParserTests
{
    private static readonly Uri Device = new("http://192.168.1.50");

    [Fact]
    public void The_device_gives_its_name_model_and_tuner_count()
    {
        var device = HdHomeRunParser.ParseDevice(HdHomeRunFixtures.Discover);

        device.Should().Be(new HdHomeRunDevice("HDHomeRun FLEX 4K", "HDHR5-4K", "1080ABCD", 4));
    }

    [Fact]
    public void A_device_that_tells_less_still_has_a_name_and_a_tuner_count_only_when_it_gave_one()
    {
        HdHomeRunParser.ParseDevice("""{"ModelNumber":"HDHR3-US","TunerCount":"2"}""").Should().Be(new HdHomeRunDevice("HDHR3-US", "HDHR3-US", null, 2));
        HdHomeRunParser.ParseDevice("{}").Should().Be(new HdHomeRunDevice("HDHomeRun", null, null, null));
        HdHomeRunParser.ParseDevice("""{"TunerCount":0}""").TunerCount.Should().BeNull();
    }

    [Fact]
    public void The_lineup_becomes_tv_channels_keyed_by_guide_number_without_the_protected_ones()
    {
        var playlistId = Guid.NewGuid();

        var channels = HdHomeRunParser.ParseLineup(HdHomeRunFixtures.Lineup, playlistId, Device);

        channels.Select(c => c.ExternalChannelId).Should().Equal("2.1", "7.1", "11.1");
        channels.Should().OnlyContain(c => c.Kind == IptvChannelKind.Tv && c.PlaylistId == playlistId);
        channels[0].Name.Should().Be("WCBS-HD");
        channels[0].StreamUrl.Should().Be("http://192.168.1.50:5004/auto/v2.1");
    }

    [Fact]
    public void A_channel_without_a_name_is_named_by_its_number_and_a_stream_address_that_points_nowhere_is_rebuilt()
    {
        var channels = HdHomeRunParser.ParseLineup(HdHomeRunFixtures.Lineup, Guid.NewGuid(), Device);

        var unnamed = channels.Single(c => c.ExternalChannelId == "11.1");
        unnamed.Name.Should().Be("11.1");
        unnamed.StreamUrl.Should().Be("http://192.168.1.50:5004/auto/v11.1");
    }

    [Fact]
    public void A_lineup_with_no_listed_address_is_built_from_the_address_the_admin_gave()
    {
        HdHomeRunParser.StreamUrl(null, "7.1", new Uri("https://hdhr.lan")).Should().Be("https://hdhr.lan:5004/auto/v7.1");
        HdHomeRunParser.StreamUrl("not a url", "5", Device).Should().Be("http://192.168.1.50:5004/auto/v5");
    }

    [Fact]
    public void An_empty_lineup_is_no_channels_and_something_else_is_not_a_tuner()
    {
        HdHomeRunParser.ParseLineup("[]", Guid.NewGuid(), Device).Should().BeEmpty();

        var notAnArray = () => HdHomeRunParser.ParseLineup("""{"error":"x"}""", Guid.NewGuid(), Device);
        var notJson = () => HdHomeRunParser.ParseDevice("<html>router login</html>");

        notAnArray.Should().Throw<IptvSourceException>();
        notJson.Should().Throw<IptvSourceException>().WithMessage("*not like an HDHomeRun*");
    }
}
