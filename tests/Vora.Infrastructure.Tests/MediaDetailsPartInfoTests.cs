using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaDetailsPartInfoTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("part-info-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaDetailsVM Details(VoraDbContext db, Guid id) =>
        db.MediaItems.Where(m => m.Id == id).Select(MediaDetailsVM.Projection).Single();

    private static Movie SeedMovie(VoraDbContext db, Action<MediaPart>? configurePart = null)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies1080" } };
        db.Set<MediaLibrary>().Add(library);
        var movie = new Movie { Id = Guid.NewGuid(), Title = "Man of War", LibraryId = library.Id };
        var part = new MediaPart
        {
            Id = Guid.NewGuid(),
            FilePath = "/movies1080/Man of War (2026)/Man of War (2026) {imdb-tt34584846} [Remux-1080p][DTS-HD MA 5.1][AVC]-Aisha.mkv",
            Container = "mkv",
            Resolution = "1080p",
            VersionName = "Remux",
            Duration = new TimeSpan(1, 50, 38),
            FileSizeBytes = 9_266_000_000,
            OverallBitrate = 11_167_000,
            MediaItemId = movie.Id,
        };
        part.VideoTracks.Add(new MediaVideoTrack { Id = Guid.NewGuid(), Codec = "hevc", Profile = "main 10", BitDepth = 10, Bitrate = 9_759_000, IsDefault = true });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { Id = Guid.NewGuid(), Codec = "subrip", Language = "eng", ExternalFilePath = "/movies1080/Man of War (2026)/Man of War (2026).en.srt", IsDownloaded = true });
        part.SubtitleTracks.Add(new MediaSubtitleTrack { Id = Guid.NewGuid(), Codec = "hdmv_pgs_subtitle", Language = "eng", StreamIndex = 3 });
        configurePart?.Invoke(part);

        movie.MediaParts.Add(part);
        db.Add(movie);
        db.SaveChanges();
        return movie;
    }

    [Fact]
    public void A_part_carries_its_container_version_and_duration()
    {
        using var db = NewContext();
        var movie = SeedMovie(db);

        var part = Details(db, movie.Id).MediaParts.Single();

        part.Container.Should().Be("mkv");
        part.VersionName.Should().Be("Remux");
        part.DurationSeconds.Should().Be(6638);
        part.BitrateKbps.Should().Be(11167);
    }

    [Fact]
    public void A_part_with_no_analysed_duration_reports_none()
    {
        using var db = NewContext();
        var movie = SeedMovie(db, p => p.Duration = null);

        Details(db, movie.Id).MediaParts.Single().DurationSeconds.Should().BeNull();
    }

    [Fact]
    public void A_video_track_carries_its_own_bitrate()
    {
        using var db = NewContext();
        var movie = SeedMovie(db);

        Details(db, movie.Id).MediaParts.Single().VideoTracks.Single().BitrateKbps.Should().Be(9759);
    }

    [Fact]
    public void A_subtitle_says_whether_it_is_a_sidecar_and_whether_vora_downloaded_it()
    {
        using var db = NewContext();
        var movie = SeedMovie(db);

        var subtitles = Details(db, movie.Id).MediaParts.Single().SubtitleTracks;

        subtitles.Should().ContainSingle(s => s.IsExternal && s.IsDownloaded && s.Codec == "subrip");
        subtitles.Should().ContainSingle(s => !s.IsExternal && !s.IsDownloaded && s.Codec == "hdmv_pgs_subtitle");
    }
}
