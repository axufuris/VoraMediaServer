using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class PictureAreaRepositoryTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("picture-area-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaRepository Repository(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private static (Guid LibraryId, MediaPart Part) SeedMovie(VoraDbContext db, string title, Action<MediaPart> configure)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } };
        db.Set<MediaLibrary>().Add(library);
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = library.Id };
        var part = new MediaPart { Id = Guid.NewGuid(), FilePath = $"/movies/{title}.mkv", Container = "mkv", Duration = TimeSpan.FromMinutes(110), MediaItemId = movie.Id };
        configure(part);
        movie.MediaParts.Add(part);
        db.Add(movie);
        db.SaveChanges();
        return (library.Id, part);
    }

    [Fact]
    public async Task A_library_pass_reads_the_main_video_of_each_film_not_checked_yet()
    {
        using var db = NewContext();
        var (libraryId, part) = SeedMovie(db, "Heat", p =>
        {
            p.VideoTracks.Add(new MediaVideoTrack { Id = Guid.NewGuid(), StreamIndex = 3, Codec = "mjpeg", Width = 600, Height = 900 });
            p.VideoTracks.Add(new MediaVideoTrack { Id = Guid.NewGuid(), StreamIndex = 0, Codec = "hevc", Width = 1920, Height = 1080, IsDefault = true });
        });

        var targets = await Repository(db).GetPictureAreaTargetsForLibraryAsync(libraryId);

        var main = part.VideoTracks.Single(t => t.Codec == "hevc");
        targets.Should().Equal(new PictureAreaTarget(main.Id, part.MediaItemId ?? Guid.Empty, "/movies/Heat.mkv", 0, 1920, 1080, 6600));
    }

    [Fact]
    public async Task A_film_already_checked_or_without_a_measured_size_is_left_out()
    {
        using var db = NewContext();
        var (libraryId, _) = SeedMovie(db, "Checked", p =>
            p.VideoTracks.Add(new MediaVideoTrack { Id = Guid.NewGuid(), Codec = "h264", Width = 1920, Height = 1080, CropTop = 0, CropRight = 0, CropBottom = 0, CropLeft = 0 }));
        var (otherLibrary, _) = SeedMovie(db, "Unmeasured", p =>
            p.VideoTracks.Add(new MediaVideoTrack { Id = Guid.NewGuid(), Codec = "h264" }));

        (await Repository(db).GetPictureAreaTargetsForLibraryAsync(libraryId)).Should().BeEmpty();
        (await Repository(db).GetPictureAreaTargetsForLibraryAsync(otherLibrary)).Should().BeEmpty();
    }

    [Fact]
    public async Task What_was_found_is_saved_on_the_track()
    {
        using var db = NewContext();
        var track = new MediaVideoTrack { Id = Guid.NewGuid(), Codec = "hevc", Width = 1920, Height = 1080 };
        var (_, part) = SeedMovie(db, "Lioness", p => p.VideoTracks.Add(track));

        await Repository(db).SavePictureCropAsync(track.Id, new PictureCrop(60, 0, 60, 0));

        var saved = await db.MediaVideoTracks.AsNoTracking().SingleAsync(t => t.Id == track.Id, TestContext.Current.CancellationToken);
        PictureCrop.Of(saved).Should().Be(new PictureCrop(60, 0, 60, 0));
        (await Repository(db).GetPictureAreaTargetsForItemAsync(part.MediaItemId ?? Guid.Empty)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_scanned_again_unchanged_keeps_its_crop_and_a_new_encode_loses_it()
    {
        using var db = NewContext();
        var track = new MediaVideoTrack { Id = Guid.NewGuid(), StreamIndex = 0, Codec = "h264", Width = 1920, Height = 1080, Bitrate = 23_000_000, CropTop = 60, CropRight = 0, CropBottom = 60, CropLeft = 0 };
        var (_, part) = SeedMovie(db, "Lioness", p => p.VideoTracks.Add(track));
        var repo = Repository(db);

        await repo.SyncMediaTracksAsync(part.Id, [new MediaVideoTrack { StreamIndex = 0, Codec = "h264", Width = 1920, Height = 1080, Bitrate = 23_000_000 }], [], []);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        PictureCrop.Of(await db.MediaVideoTracks.AsNoTracking().SingleAsync(t => t.Id == track.Id, TestContext.Current.CancellationToken)).Should().Be(new PictureCrop(60, 0, 60, 0));

        await repo.SyncMediaTracksAsync(part.Id, [new MediaVideoTrack { StreamIndex = 0, Codec = "hevc", Width = 1920, Height = 1080, Bitrate = 3_700_000 }], [], []);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        PictureCrop.Of(await db.MediaVideoTracks.AsNoTracking().SingleAsync(t => t.Id == track.Id, TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
