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

        device.Should().Be(new HdHomeRunDevice("HDHomeRun FLEX 4K", "HDHR5-4K", "1080ABCD", 4, "20240101"));
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
    public void A_channel_the_lineup_gives_a_codec_for_carries_it_as_ffprobe_spells_it()
    {
        var channels = HdHomeRunParser.ParseLineup(HdHomeRunFixtures.Lineup, Guid.NewGuid(), Device);

        channels.Single(c => c.ExternalChannelId == "2.1").VideoCodec.Should().Be("mpeg2video");
        channels.Single(c => c.ExternalChannelId == "7.1").VideoCodec.Should().BeNull("the lineup names no codec for it");
    }

    [Theory]
    [InlineData("MPEG2", "mpeg2video")]
    [InlineData("mpeg2", "mpeg2video")]
    [InlineData("H264", "h264")]
    [InlineData("HEVC", "hevc")]
    [InlineData("H265", "hevc")]
    [InlineData("VP9", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_listed_codec_is_a_hint_only_when_it_is_one_this_server_knows(string? listed, string? expected)
    {
        HdHomeRunParser.CodecName(listed).Should().Be(expected);
    }

    [Fact]
    public void A_lineup_with_no_listed_address_is_built_from_the_address_the_admin_gave()
    {
        HdHomeRunParser.StreamUrl(null, "7.1", new Uri("https://hdhr.lan")).Should().Be("https://hdhr.lan:5004/auto/v7.1");
        HdHomeRunParser.StreamUrl("not a url", "5", Device).Should().Be("http://192.168.1.50:5004/auto/v5");
    }

    [Fact]
    public void The_status_lists_each_tuner_with_what_it_is_tuned_to_and_how_well()
    {
        const string status = """
            [
             {"Resource":"tuner1"},
             {"Resource":"tuner0","VctNumber":"7.1","VctName":"WABC-DT","Frequency":473000000,"SignalStrengthPercent":84,"SignalQualityPercent":100,"SymbolQualityPercent":98,"TargetIP":"192.168.1.20","NetworkRate":19393088},
             {"Resource":"system","Version":1}
            ]
            """;

        var tuners = HdHomeRunParser.ParseStatus(status);

        tuners.Should().Equal(
            new HdHomeRunTuner(0, true, "7.1", "WABC-DT", 473000000, 84, 100, 98, 19393088, "192.168.1.20"),
            new HdHomeRunTuner(1, false, null, null, null, null, null, null, null, null));
    }

    [Fact]
    public void A_tuner_is_in_use_when_it_has_a_channel_a_frequency_or_a_receiver_and_idle_when_it_has_none()
    {
        var tuners = HdHomeRunParser.ParseStatus("""
            [{"Resource":"tuner0","Frequency":0},{"Resource":"tuner1","Frequency":605000000},{"Resource":"tuner2","TargetIP":"192.168.1.9"},{"Resource":"tuner3","VctName":"KRON"}]
            """);

        tuners.Select(t => t.InUse).Should().Equal(false, true, true, true);
        tuners[0].FrequencyHz.Should().BeNull();
    }

    [Fact]
    public void Percentages_stay_between_nothing_and_everything_and_numbers_sent_as_text_are_read()
    {
        var tuner = HdHomeRunParser.ParseStatus("""
            [{"Resource":"tuner0","VctNumber":5,"SignalStrengthPercent":140,"SignalQualityPercent":"72","SymbolQualityPercent":-3,"Frequency":"473000000"}]
            """).Single();

        tuner.ChannelNumber.Should().Be("5");
        tuner.SignalStrengthPercent.Should().Be(100);
        tuner.SignalQualityPercent.Should().Be(72);
        tuner.SymbolQualityPercent.Should().Be(0);
        tuner.FrequencyHz.Should().Be(473000000);
    }

    [Fact]
    public void A_status_that_is_not_a_list_is_not_a_tuner_status()
    {
        var notAList = () => HdHomeRunParser.ParseStatus("""{"Resource":"tuner0"}""");

        notAList.Should().Throw<IptvSourceException>().WithMessage("*tuner status*");
        HdHomeRunParser.ParseStatus("[]").Should().BeEmpty();
    }

    [Fact]
    public void The_guide_address_carries_the_devices_authorization_escaped_and_a_device_without_one_has_none()
    {
        HdHomeRunParser.DeviceAuth(HdHomeRunFixtures.Discover).Should().Be("secret-auth");
        HdHomeRunParser.DeviceAuth("""{"FriendlyName":"Old"}""").Should().BeNull();
        HdHomeRunParser.GuideUrl("a b+c/d=").Should().Be("https://api.hdhomerun.com/api/xmltv?DeviceAuth=a%20b%2Bc%2Fd%3D");
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
