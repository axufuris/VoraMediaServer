using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class StreamDecisionLogTests
{
    private static ClientDevice Phone() => new()
    {
        DeviceType = "iPhone",
        ClientName = "Vora iOS",
        OperatingSystem = "iOS 27.2",
        DeviceName = "Someone's iPhone",
        LastIpAddress = "192.0.2.77",
        SupportedVideoCodecs = ["h264", "hevc"],
        SupportedAudioCodecs = ["aac", "eac3"],
        SupportedContainers = ["mp4", "hls"],
        SupportedHdrFormats = ["HDR10"],
        MaxVideoBitDepth = 10,
        MaxAudioChannels = 6
    };

    private static DeviceCapsDto Caps(string[]? hls) =>
        new(["h264", "hevc"], ["aac"], ["mp4", "hls"], 6, 0, 0, HlsVideoCodecs: hls);

    private static MediaPartStreamInfoDto Part() => new() { Container = "matroska", FilePath = "/data/movies/Secret Title (2001)/Secret Title.mkv" };

    private static TrackStreamInfoDto Video() => new() { Codec = "hevc", Width = 1918, Height = 816, BitDepth = 10 };

    private static TrackStreamInfoDto Audio() => new() { Codec = "aac", Channels = 6 };

    private static StreamDecisionDto Decision() => new()
    {
        Strategy = StreamStrategy.Remux,
        VideoStrategy = "Copy",
        AudioStrategy = "Copy",
        TargetVideoCodec = "hevc",
        TargetAudioCodec = "aac",
        TargetContainer = "hls",
        Reasons = ["Container 'mkv' unsupported. Copying streams to HLS.", "Video Direct Stream: Supported."]
    };

    [Fact]
    public void Says_what_the_client_claimed_the_file_is_and_what_was_chosen()
    {
        var line = StreamDecisionLog.Describe(Phone(), Caps(["h264", "hevc"]), Part(), Video(), Audio(), Decision());

        line.Should().Contain("client=iPhone/Vora iOS iOS 27.2");
        line.Should().Contain("claims: video=[h264,hevc]").And.Contain("hlsVideo=[h264,hevc] ").And.Contain("bitDepth=10").And.Contain("hdr=[HDR10]");
        line.Should().Contain("source: container=matroska video=hevc 1918x816 bitDepth=10 hdr=- audio=aac channels=6");
        line.Should().Contain("chose: strategy=Remux video=Copy->hevc audio=Copy->aac container=hls");
        line.Should().EndWith("reasons: Container 'mkv' unsupported. Copying streams to HLS. | Video Direct Stream: Supported.");
    }

    [Fact]
    public void Marks_an_hls_codec_list_the_client_did_not_report_as_the_device_type_default()
    {
        var line = StreamDecisionLog.Describe(Phone(), Caps(null), Part(), Video(), Audio(), Decision());

        line.Should().Contain("hlsVideo=[h264,hevc,mpeg2video](default for this device type)");
    }

    [Fact]
    public void Gives_a_browser_h264_only_when_it_reports_nothing()
    {
        var browser = new ClientDevice { DeviceType = "Browser", ClientName = "Vora Web" };

        var line = StreamDecisionLog.Describe(browser, null, null, null, null, new StreamDecisionDto());

        line.Should().Contain("hlsVideo=[h264](default for this device type)");
    }

    [Fact]
    public void Names_no_file_path_address_or_device_name()
    {
        var line = StreamDecisionLog.Describe(Phone(), Caps(["h264"]), Part(), Video(), Audio(), Decision());

        line.Should().NotContain("/data/").And.NotContain("Secret Title").And.NotContain("192.0.2.77").And.NotContain("Someone's iPhone");
    }

    [Fact]
    public void Shows_a_dash_for_what_is_unknown()
    {
        var line = StreamDecisionLog.Describe(new ClientDevice(), null, null, null, null, new StreamDecisionDto());

        line.Should().Contain("source: container=- video=- - bitDepth=- hdr=- audio=- channels=-").And.EndWith("reasons: -");
    }
}
