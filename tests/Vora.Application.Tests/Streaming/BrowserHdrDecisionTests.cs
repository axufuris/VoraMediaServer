using Vora.Application.Settings;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class BrowserHdrDecisionTests
{
    private static ClientDevice Browser() => new()
    {
        DeviceType = "Browser",
        MaxAudioChannels = 2,
        MaxVideoBitDepth = 10,
        SupportedVideoCodecs = ["hevc", "h264"],
        SupportedAudioCodecs = ["aac"],
        SupportedContainers = ["mp4"],
        SupportedHdrFormats = ["HDR10"]
    };

    private static MediaStreamInfoDto Titanic() => new()
    {
        Id = Guid.NewGuid(),
        Parts =
        [
            new()
            {
                Id = Guid.NewGuid(),
                Container = "mkv",
                Resolution = "2160p",
                OverallBitrate = 13_667_191,
                VideoTracks =
                [
                    new()
                    {
                        Id = Guid.NewGuid(), Codec = "hevc", IsDefault = true,
                        Width = 3832, Height = 1640, FrameRate = 23.976,
                        Bitrate = 8_187_191, BitDepth = 10, HdrType = "HDR10"
                    }
                ],
                AudioTracks =
                [
                    new() { Id = Guid.NewGuid(), Codec = "dts", Channels = 6, IsDefault = true, Language = "eng" }
                ]
            }
        ]
    };

    private static Task<StreamDecisionDto> DecideAsync(ClientDevice client, int maxResolution = 0, int maxBandwidthKbps = 0, string[]? hlsVideoCodecs = null)
    {
        var settings = Substitute.For<ISystemSettingsRepository>();
        settings.GetSettingsAsync().Returns(new ServerSetting
        {
            StreamingProfile = StreamingProfile.ClientPreference,
            HdrTranscodeDownscale = "Never"
        });

        return new BestPathDecisionManager(settings).DetermineBestPathAsync(
            client, Titanic(), maxBandwidthKbps, "test",
            requestedMaxResolution: maxResolution,
            hlsVideoCodecs: hlsVideoCodecs ?? ["h264", "hevc"]);
    }

    [Fact]
    public async Task Original_converts_dts_audio_and_remuxes_without_converting_the_hdr_videoAsync()
    {
        var decision = await DecideAsync(Browser());

        decision.VideoStrategy.Should().Be("DirectStream");
        decision.TargetVideoCodec.Should().Be("hevc");
        decision.OutputResolution.Should().Be("2160p");
        decision.OutputHdrType.Should().Be("HDR10");
        decision.AudioStrategy.Should().Be("Transcode");
        decision.TargetAudioCodec.Should().Be("aac");
        decision.TargetAudioChannels.Should().Be(2);
        decision.TargetContainer.Should().Be("hls");
        decision.RequiresSubtitleBurnIn.Should().BeFalse();
    }

    [Theory]
    [InlineData("hdr")]
    [InlineData("codec")]
    [InlineData("bit-depth")]
    [InlineData("hls")]
    [InlineData("resolution")]
    [InlineData("bandwidth")]
    public async Task Original_still_converts_video_when_a_real_playback_constraint_requires_itAsync(string constraint)
    {
        var client = Browser();
        if (constraint == "hdr") client.SupportedHdrFormats = [];
        if (constraint == "codec") client.SupportedVideoCodecs = ["h264"];
        if (constraint == "bit-depth") client.MaxVideoBitDepth = 8;

        var decision = await DecideAsync(
            client,
            maxResolution: constraint == "resolution" ? 1080 : 0,
            maxBandwidthKbps: constraint == "bandwidth" ? 4000 : 0,
            hlsVideoCodecs: constraint == "hls" ? ["h264"] : null);

        decision.VideoStrategy.Should().Be("Transcode");
        decision.OutputHdrType.Should().Be("SDR");
        if (constraint == "resolution")
        {
            decision.OutputWidth.Should().BeLessThanOrEqualTo(1920);
            decision.OutputHeight.Should().BeLessThanOrEqualTo(1080);
        }
    }
}
