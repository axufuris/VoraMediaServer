using Vora.Application.Analysis.Results;
using Vora.Application.Streaming;
using Vora.Application.Streaming.Dtos;

namespace Vora.Application.Tests.Streaming;

public class VideoTrackGeometryFillTests
{
    private static MediaPartStreamInfoDto Part(string? hdrType, int? width = null, string filePath = "/movies/minions.mkv") => new()
    {
        Id = Guid.NewGuid(),
        FilePath = filePath,
        VideoTracks = [new TrackStreamInfoDto { Id = Guid.NewGuid(), StreamIndex = 0, Codec = "hevc", HdrType = hdrType, Width = width, Height = width is null ? null : 1540 }],
    };

    [Fact]
    public void A_part_scanned_before_sizes_were_stored_is_read_again()
    {
        VideoTrackGeometryFill.IsMissing(Part("HDR10")).Should().BeTrue();
        VideoTrackGeometryFill.IsMissing(Part("HDR10", width: 3840)).Should().BeFalse();
        VideoTrackGeometryFill.IsMissing(Part("HDR10", filePath: "")).Should().BeFalse();
    }

    [Fact]
    public void Stores_the_size_and_frame_rate_the_probe_found()
    {
        var part = Part("HDR10Plus");

        var updates = VideoTrackGeometryFill.Apply(part, [new VideoStreamFacts(0, 3840, 1540, 23.976, "HDR10")]);

        var track = part.VideoTracks[0];
        updates.Should().Equal(new VideoTrackGeometry(track.Id, 3840, 1540, 23.976, "HDR10Plus"));
        track.Width.Should().Be(3840);
        track.Height.Should().Be(1540);
        track.HdrType.Should().Be("HDR10Plus");
    }

    [Fact]
    public void Corrects_a_bare_dolby_vision_label_while_it_is_there()
    {
        var part = Part("DoVi");

        var updates = VideoTrackGeometryFill.Apply(part, [new VideoStreamFacts(0, 3840, 1540, 23.976, null)]);

        updates.Single().HdrType.Should().BeNull();
        part.VideoTracks[0].HdrType.Should().BeNull();
    }

    [Fact]
    public void Leaves_a_track_the_probe_said_nothing_about()
    {
        var part = Part("HDR10");

        VideoTrackGeometryFill.Apply(part, [new VideoStreamFacts(4, 3840, 1540, 24, null), new VideoStreamFacts(0, null, null, null, null)]).Should().BeEmpty();
        part.VideoTracks[0].Width.Should().BeNull();
    }
}
