using Vora.Application.Streaming;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class VideoBitrateTests
{
    private static readonly FrameSize Scope4k = new(3840, 1540);
    private static readonly FrameSize ScopeFor1440 = new(2560, 1026);

    // Issue #145's file: 6.4 Mbps 10-bit HEVC at 3840x1540, sent to a 1440p
    // browser as H.264. The picture is 44% as big, H.264 needs about two
    // thirds more bits than HEVC and 8-bit a fifth more than 10-bit.
    [Fact]
    public void Scales_a_hevc_source_by_its_smaller_picture_then_for_h264_and_8_bit()
    {
        var rate = VideoBitrate.For(6385, "hevc", 10, Scope4k, ScopeFor1440, VideoCodec.H264, ceilingKbps: 0, profileCapKbps: 0);

        rate.TargetKbps.Should().BeCloseTo(6948, 5);
        rate.MaxKbps.Should().BeCloseTo(8175, 5);
    }

    [Fact]
    public void Aims_at_85_percent_of_a_connection_limit_and_peaks_no_higher_than_the_limit()
    {
        var rate = VideoBitrate.For(6385, "hevc", 10, Scope4k, ScopeFor1440, VideoCodec.H264, ceilingKbps: 8000, profileCapKbps: 0);

        rate.Should().Be(new VideoRate(6800, 8000));
    }

    [Fact]
    public void Leaves_some_room_above_the_target_when_there_is_no_limit()
    {
        var size = new FrameSize(1920, 1080);
        var rate = VideoBitrate.For(8000, "h264", 8, size, size, VideoCodec.H264, ceilingKbps: 0, profileCapKbps: 0);

        rate.Should().Be(new VideoRate(8000, 9412));
    }

    [Theory]
    [InlineData(400, 1600)]
    [InlineData(900, 2700)]
    [InlineData(1500, 3750)]
    [InlineData(2500, 5000)]
    public void Gives_a_low_bitrate_source_more_room_because_a_fast_reencode_of_it_looks_worse(int sourceKbps, int expectedTarget)
    {
        var size = new FrameSize(1280, 720);
        VideoBitrate.For(sourceKbps, "h264", 8, size, size, VideoCodec.H264, 0, 0).TargetKbps.Should().Be(expectedTarget);
    }

    [Fact]
    public void Does_not_scale_a_very_high_bitrate_for_the_codec()
    {
        var size = new FrameSize(3840, 2160);
        VideoBitrate.For(40000, "hevc", 8, size, size, VideoCodec.H264, 0, 0).TargetKbps.Should().Be(40000);
    }

    [Fact]
    public void Never_raises_the_bitrate_for_a_more_efficient_output_codec()
    {
        var size = new FrameSize(1920, 1080);
        VideoBitrate.For(8000, "h264", 8, size, size, VideoCodec.Hevc, 0, 0).TargetKbps.Should().Be(8000);
    }

    [Fact]
    public void A_bandwidth_optimized_server_caps_the_target_at_its_ladder()
    {
        VideoBitrate.For(6385, "hevc", 10, Scope4k, ScopeFor1440, VideoCodec.H264, ceilingKbps: 0, profileCapKbps: 5000).TargetKbps.Should().Be(5000);
    }

    [Fact]
    public void Knows_nothing_about_the_picture_size_and_still_compensates_for_the_codec()
    {
        VideoBitrate.For(6000, "hevc", 8, null, null, VideoCodec.H264, 0, 0).TargetKbps.Should().Be(10000);
    }

    [Fact]
    public void Stops_at_a_sane_maximum()
    {
        var size = new FrameSize(3840, 2160);
        VideoBitrate.For(900_000, "h264", 8, size, size, VideoCodec.H264, 0, 0).TargetKbps.Should().Be(VideoBitrate.MaxSaneKbps);
    }

    [Fact]
    public void The_transcoder_recovers_the_target_from_the_cap_it_is_given()
    {
        VideoBitrate.TargetFromCap(10000).Should().Be(8500);
        VideoBitrate.TargetFromCap(8000).Should().Be(6800);
    }
}
