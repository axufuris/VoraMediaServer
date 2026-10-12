using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Media;

public class CoverImageTracksTests
{
    private static MediaVideoTrack Track(int streamIndex, string codec, bool isDefault = false) =>
        new() { StreamIndex = streamIndex, Codec = codec, IsDefault = isDefault };

    [Theory]
    [InlineData("mjpeg")]
    [InlineData("MJPEG")]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("tiff")]
    [InlineData("webp")]
    public void Image_formats_are_still_image_codecs(string codec)
    {
        CoverImageTracks.IsStillImageCodec(codec).Should().BeTrue();
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("hevc")]
    [InlineData("av1")]
    [InlineData("mpeg2video")]
    [InlineData(null)]
    public void Video_formats_are_not(string? codec)
    {
        CoverImageTracks.IsStillImageCodec(codec).Should().BeFalse();
    }

    [Fact]
    public void A_cover_image_beside_the_video_is_left_out()
    {
        var tracks = new[] { Track(0, "h264", isDefault: true), Track(5, "mjpeg") };

        CoverImageTracks.Without(tracks, track => track.Codec).Should().ContainSingle().Which.StreamIndex.Should().Be(0);
    }

    [Fact]
    public void A_file_whose_only_video_is_motion_jpeg_keeps_it()
    {
        var tracks = new[] { Track(0, "mjpeg", isDefault: true) };

        CoverImageTracks.Without(tracks, track => track.Codec).Should().ContainSingle();
    }

    [Fact]
    public void The_main_track_is_the_video_even_when_the_cover_is_listed_first()
    {
        var tracks = new[] { Track(5, "png"), Track(0, "hevc") };

        CoverImageTracks.MainVideoTrack(tracks)!.Codec.Should().Be("hevc");
    }

    [Fact]
    public void The_main_track_is_the_default_one_then_the_first_in_the_file()
    {
        CoverImageTracks.MainVideoTrack([Track(1, "h264"), Track(0, "hevc")])!.StreamIndex.Should().Be(0);
        CoverImageTracks.MainVideoTrack([Track(0, "hevc"), Track(1, "h264", isDefault: true)])!.StreamIndex.Should().Be(1);
    }

    [Fact]
    public void A_part_without_video_tracks_has_no_main_track()
    {
        CoverImageTracks.MainVideoTrack([]).Should().BeNull();
        CoverImageTracks.MainVideoTrack(null).Should().BeNull();
    }
}
