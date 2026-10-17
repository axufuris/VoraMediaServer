using Vora.Application.Streaming;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Streaming;

public class OutputSizeTests
{
    [Theory]
    [InlineData(3840, 1540, 2560, 1440, 2560, 1026)]
    [InlineData(3840, 1540, 3440, 1440, 3440, 1380)]
    [InlineData(3840, 2160, 1920, 1080, 1920, 1080)]
    [InlineData(1920, 817, 1280, 720, 1280, 544)]
    [InlineData(1080, 1920, 2560, 1440, 810, 1440)]
    public void Fits_the_picture_inside_the_box_keeping_its_shape_in_even_numbers(int width, int height, int boxWidth, int boxHeight, int expectedWidth, int expectedHeight)
    {
        OutputSize.Fit(new FrameSize(width, height), new FrameSize(boxWidth, boxHeight))
            .Should().Be(new FrameSize(expectedWidth, expectedHeight));
    }

    [Fact]
    public void Never_makes_a_picture_bigger()
    {
        OutputSize.Fit(new FrameSize(1920, 800), new FrameSize(3840, 2160)).Should().Be(new FrameSize(1920, 800));
    }

    [Fact]
    public void Keeps_within_what_a_hardware_encoder_takes()
    {
        OutputSize.Fit(new FrameSize(7680, 4320), new FrameSize(7680, 4320)).Should().Be(new FrameSize(4096, 2304));
    }

    [Fact]
    public void Reads_the_measured_size_first_and_the_file_name_label_only_without_one()
    {
        OutputSize.Source(3840, 1540, "1080p").Should().Be(new FrameSize(3840, 1540));
        OutputSize.Source(null, null, "2160p").Should().Be(new FrameSize(3840, 2160));
        OutputSize.Source(null, null, "4k").Should().Be(new FrameSize(3840, 2160));
        OutputSize.Source(null, null, null).Should().BeNull();
    }

    [Fact]
    public void A_client_that_gives_only_a_height_gets_a_widescreen_box_and_one_that_gives_nothing_gets_4k()
    {
        OutputSize.ClientBox(0, 1440).Should().Be(new FrameSize(2560, 1440));
        OutputSize.ClientBox(3440, 1440).Should().Be(new FrameSize(3440, 1440));
        OutputSize.ClientBox(0, 0).Should().Be(new FrameSize(3840, 2160));
    }

    [Theory]
    [InlineData(3840, 1540, 2160)]
    [InlineData(1920, 800, 1080)]
    [InlineData(3840, 2160, 2160)]
    [InlineData(1080, 1920, 1080)]
    [InlineData(2388, 1344, 1344)]
    public void Names_a_size_by_its_widescreen_class_whatever_its_shape(int width, int height, int expected)
    {
        OutputSize.HeightClass(new FrameSize(width, height)).Should().Be(expected);
    }

    [Theory]
    [InlineData(4000, 23.976, 1280)]
    [InlineData(4000, 30.0, 1280)]
    [InlineData(20000, 24.0, 2560)]
    [InlineData(1000, 30.0, 768)]
    [InlineData(60000, 24.0, int.MaxValue)]
    [InlineData(0, 24.0, int.MaxValue)]
    public void Narrows_the_picture_to_what_the_connection_can_carry(int kbps, double frameRate, int expectedWidth)
    {
        OutputSize.MaxWidthForBitrate(kbps, frameRate, VideoCodec.H264).Should().Be(expectedWidth);
    }

    [Fact]
    public void A_faster_frame_rate_needs_more_bits_for_the_same_width()
    {
        OutputSize.MaxWidthForBitrate(6000, 60, VideoCodec.H264).Should().BeLessThan(OutputSize.MaxWidthForBitrate(6000, 24, VideoCodec.H264));
    }
}
