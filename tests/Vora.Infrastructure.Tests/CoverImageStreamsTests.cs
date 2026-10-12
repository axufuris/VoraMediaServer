using System.Text.Json;
using Vora.Infrastructure.Analysis;

namespace Vora.Infrastructure.Tests;

public class CoverImageStreamsTests
{
    private static VideoStreamRoles Read(string streams) => CoverImageStreams.Read(JsonDocument.Parse(streams).RootElement);

    [Fact]
    public void A_cover_stored_as_a_video_track_is_a_cover_image()
    {
        var roles = Read("""
        [
            { "index": 0, "codec_type": "video", "codec_name": "h264", "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 1, "codec_type": "audio", "codec_name": "eac3", "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 4, "codec_type": "subtitle", "codec_name": "subrip", "disposition": { "default": 0, "attached_pic": 0 } },
            { "index": 5, "codec_type": "video", "codec_name": "mjpeg", "disposition": { "default": 0, "attached_pic": 0 } }
        ]
        """);

        roles.Pictures.Should().Equal(0);
        roles.CoverImages.Should().Equal(5);
        roles.IsCoverImage(5).Should().BeTrue();
        roles.IsCoverImage(0).Should().BeFalse();
        roles.IgnoreCoverImagesArguments.Should().Equal("-discard:5", "all");
    }

    [Fact]
    public void An_attached_picture_is_a_cover_image_whatever_its_codec()
    {
        var roles = Read("""
        [
            { "index": 0, "codec_type": "video", "codec_name": "hevc", "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 6, "codec_type": "video", "codec_name": "png", "disposition": { "default": 0, "attached_pic": 1 } },
            { "index": 7, "codec_type": "video", "codec_name": "mjpeg", "disposition": { "default": 0, "attached_pic": 1 } }
        ]
        """);

        roles.Pictures.Should().Equal(0);
        roles.CoverImages.Should().Equal(6, 7);
        roles.IgnoreCoverImagesArguments.Should().Equal("-discard:6", "all", "-discard:7", "all");
    }

    [Fact]
    public void A_song_with_embedded_artwork_has_a_cover_and_no_picture_to_protect()
    {
        var roles = Read("""
        [
            { "index": 0, "codec_type": "audio", "codec_name": "flac", "disposition": { "default": 0, "attached_pic": 0 } },
            { "index": 1, "codec_type": "video", "codec_name": "mjpeg", "disposition": { "default": 0, "attached_pic": 1 } }
        ]
        """);

        roles.Pictures.Should().BeEmpty();
        roles.CoverImages.Should().Equal(1);
        roles.IgnoreCoverImagesArguments.Should().BeEmpty();
    }

    [Fact]
    public void A_file_whose_only_video_is_motion_jpeg_is_a_picture()
    {
        var roles = Read("""
        [
            { "index": 0, "codec_type": "video", "codec_name": "mjpeg", "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 1, "codec_type": "audio", "codec_name": "pcm_s16le", "disposition": { "default": 1, "attached_pic": 0 } }
        ]
        """);

        roles.Pictures.Should().Equal(0);
        roles.CoverImages.Should().BeEmpty();
        roles.IgnoreCoverImagesArguments.Should().BeEmpty();
    }

    [Fact]
    public void A_file_without_cover_images_needs_no_extra_arguments()
    {
        var roles = Read("""
        [
            { "index": 0, "codec_type": "video", "codec_name": "h264" },
            { "index": 1, "codec_type": "audio", "codec_name": "aac" }
        ]
        """);

        roles.Pictures.Should().Equal(0);
        roles.IgnoreCoverImagesArguments.Should().BeEmpty();
    }

    [Fact]
    public void Anything_that_is_not_a_stream_list_has_no_video()
    {
        var roles = Read("{}");

        roles.Pictures.Should().BeEmpty();
        roles.CoverImages.Should().BeEmpty();
    }
}
