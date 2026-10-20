using Vora.Application.Analysis;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Tests.Analysis;

public class PictureCropTests
{
    private static CropBox Box(int x, int y, int width, int height) => new(x, y, width, height);

    [Fact]
    public void A_letterboxed_picture_keeps_the_bars_it_has_in_every_sample()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(0, 60, 1920, 960), Box(0, 62, 1920, 956), Box(0, 60, 1920, 960)]);

        crop.Should().Be(new PictureCrop(60, 0, 60, 0));
    }

    [Fact]
    public void A_dark_scene_does_not_eat_into_the_picture_the_other_samples_show()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(0, 140, 1920, 800), Box(200, 300, 1500, 400), Box(0, 140, 1920, 800)]);

        crop.Should().Be(new PictureCrop(140, 0, 140, 0));
    }

    [Fact]
    public void A_pillarboxed_picture_has_bars_at_the_sides()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(240, 0, 1440, 1080), Box(240, 0, 1440, 1080), Box(242, 0, 1436, 1080)]);

        crop.Should().Be(new PictureCrop(0, 240, 0, 240));
    }

    [Fact]
    public void A_sliver_at_the_edge_is_not_a_bar()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(0, 4, 1920, 1072), Box(2, 8, 1916, 1066), Box(0, 4, 1920, 1072)]);

        crop.IsNone.Should().BeTrue();
    }

    [Fact]
    public void Too_few_usable_samples_mean_no_bars_rather_than_a_guess()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(0, 60, 1920, 960), Box(0, 0, 10, 10), Box(0, 500, 1920, 4)]);

        crop.Should().Be(PictureCrop.None);
    }

    [Fact]
    public void A_box_outside_the_frame_is_not_used()
    {
        var crop = PictureCrop.FromBoxes(1920, 1080, [Box(0, 60, 1920, 960), Box(0, 60, 1920, 960), Box(0, 60, 1920, 960), Box(0, 0, 3840, 2160)]);

        crop.Should().Be(new PictureCrop(60, 0, 60, 0));
    }

    [Fact]
    public void A_track_reads_and_writes_its_crop_and_starts_with_none()
    {
        var track = new MediaVideoTrack();
        PictureCrop.Of(track).Should().BeNull();

        new PictureCrop(60, 0, 60, 0).ApplyTo(track);

        PictureCrop.Of(track).Should().Be(new PictureCrop(60, 0, 60, 0));
        PictureCrop.Forget(track);
        PictureCrop.Of(track).Should().BeNull();
    }

    [Fact]
    public void A_crop_is_forgotten_when_the_file_was_encoded_again()
    {
        var before = new MediaVideoTrack { Codec = "h264", Width = 1920, Height = 1080, Bitrate = 23_000_000 };

        PictureCrop.StreamChanged(before, new MediaVideoTrack { Codec = "h264", Width = 1920, Height = 1080, Bitrate = 23_000_000 }).Should().BeFalse();
        PictureCrop.StreamChanged(before, new MediaVideoTrack { Codec = "hevc", Width = 1920, Height = 1080, Bitrate = 23_000_000 }).Should().BeTrue();
        PictureCrop.StreamChanged(before, new MediaVideoTrack { Codec = "h264", Width = 1920, Height = 960, Bitrate = 23_000_000 }).Should().BeTrue();
        PictureCrop.StreamChanged(before, new MediaVideoTrack { Codec = "h264", Width = 1920, Height = 1080, Bitrate = 8_000_000 }).Should().BeTrue();
    }
}
