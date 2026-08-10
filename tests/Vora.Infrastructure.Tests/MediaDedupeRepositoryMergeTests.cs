using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaDedupeRepositoryMergeTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("dedupe-merge-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static TvShow AddShow(VoraDbContext db, Guid libraryId, string tmdb, DateTime added)
    {
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Hawkeye", LibraryId = libraryId, TmdbId = tmdb, AddedAt = added };
        db.Add(show);
        return show;
    }

    private static Episode AddEpisode(VoraDbContext db, Guid libraryId, TvShow show, int seasonNumber, int episodeNumber, string filePath, string resolution)
    {
        var season = db.Set<Season>().Local.FirstOrDefault(s => s.TvShowId == show.Id && s.SeasonNumber == seasonNumber);
        if (season == null)
        {
            season = new Season { Id = Guid.NewGuid(), Title = $"Season {seasonNumber}", SeasonNumber = seasonNumber, TvShowId = show.Id, LibraryId = libraryId };
            db.Add(season);
        }
        var ep = new Episode { Id = Guid.NewGuid(), Title = $"E{episodeNumber}", EpisodeNumber = episodeNumber, SeasonId = season.Id, LibraryId = libraryId };
        db.Add(ep);
        db.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = filePath, Resolution = resolution, MediaItemId = ep.Id, Container = "mkv" });
        return ep;
    }

    [Fact]
    public async Task Merges_two_show_rows_moving_parts_and_watch_state()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Add(new MediaLibrary { Id = libraryId, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });
        db.Add(new UserProfile { Id = profileId, Name = "Me", UserId = Guid.NewGuid() });

        var keep = AddShow(db, libraryId, "88329", new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc));
        var k1 = AddEpisode(db, libraryId, keep, 1, 1, "/tv/1080p/Hawkeye/S01E01.mkv", "1080p");
        var k2 = AddEpisode(db, libraryId, keep, 1, 2, "/tv/1080p/Hawkeye/S01E02.mkv", "1080p");

        var drop = AddShow(db, libraryId, "88329", new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));
        var d1 = AddEpisode(db, libraryId, drop, 1, 1, "/tv/4K/Hawkeye/S01E01.mkv", "2160p");
        var d2 = AddEpisode(db, libraryId, drop, 1, 2, "/tv/4K/Hawkeye/S01E02.mkv", "2160p");

        db.Add(new UserMediaState { ProfileId = profileId, MediaItemId = k1.Id, IsPlayed = false, ResumePositionSeconds = 50 });
        db.Add(new UserMediaState { ProfileId = profileId, MediaItemId = d1.Id, IsPlayed = true, ResumePositionSeconds = 200 });
        db.Add(new UserMediaState { ProfileId = profileId, MediaItemId = d2.Id, IsPlayed = true, ResumePositionSeconds = 300 });
        db.SaveChanges();

        var keepId = keep.Id; var dropId = drop.Id;
        var result = await new MediaDedupeRepository(db).MergeDuplicateTvShowsAsync(libraryId);

        Assert.Equal(1, result.GroupsMerged);
        Assert.Equal(1, result.ShowsRemoved);
        Assert.Equal(2, result.PartsMoved);

        // only the keeper show survives
        Assert.Null(await db.Set<TvShow>().FirstOrDefaultAsync(t => t.Id == dropId));
        Assert.NotNull(await db.Set<TvShow>().FirstOrDefaultAsync(t => t.Id == keepId));

        // each keeper episode now carries both the 1080p and 4K parts
        Assert.Equal(2, await db.Set<MediaPart>().CountAsync(p => p.MediaItemId == k1.Id));
        Assert.Equal(2, await db.Set<MediaPart>().CountAsync(p => p.MediaItemId == k2.Id));
        Assert.Equal(0, await db.Set<MediaPart>().CountAsync(p => p.MediaItemId == d1.Id || p.MediaItemId == d2.Id));

        // E1 conflict merged: played wins, max resume kept
        var e1 = await db.Set<UserMediaState>().SingleAsync(s => s.MediaItemId == k1.Id && s.ProfileId == profileId);
        Assert.True(e1.IsPlayed);
        Assert.Equal(200, e1.ResumePositionSeconds);

        // E2 watched only on the 4K side survives, moved onto the keeper episode
        var e2 = await db.Set<UserMediaState>().SingleAsync(s => s.MediaItemId == k2.Id && s.ProfileId == profileId);
        Assert.True(e2.IsPlayed);
        Assert.Equal(300, e2.ResumePositionSeconds);
        Assert.False(await db.Set<UserMediaState>().AnyAsync(s => s.MediaItemId == d2.Id));
    }

    [Fact]
    public async Task Moves_drop_only_episode_onto_the_keeper()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        db.Add(new MediaLibrary { Id = libraryId, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });

        var keep = AddShow(db, libraryId, "500", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddEpisode(db, libraryId, keep, 1, 1, "/tv/1080p/x/S01E01.mkv", "1080p");
        AddEpisode(db, libraryId, keep, 1, 2, "/tv/1080p/x/S01E02.mkv", "1080p");

        var drop = AddShow(db, libraryId, "500", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        AddEpisode(db, libraryId, drop, 1, 1, "/tv/4K/x/S01E01.mkv", "2160p");
        var d3 = AddEpisode(db, libraryId, drop, 1, 3, "/tv/4K/x/S01E03.mkv", "2160p"); // only exists on the drop side
        db.SaveChanges();

        var keepId = keep.Id;
        await new MediaDedupeRepository(db).MergeDuplicateTvShowsAsync(libraryId);

        // the drop-only episode is preserved under the keeper's season
        var moved = await db.Set<Episode>().Include(e => e.Season).FirstAsync(e => e.Id == d3.Id);
        Assert.Equal(keepId, moved.Season.TvShowId);
        Assert.Equal(1, await db.Set<MediaPart>().CountAsync(p => p.MediaItemId == d3.Id));
    }

    [Fact]
    public async Task Does_not_merge_shows_in_different_libraries()
    {
        using var db = NewContext();
        var libA = Guid.NewGuid();
        var libB = Guid.NewGuid();
        db.Add(new MediaLibrary { Id = libA, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/a" } });
        db.Add(new MediaLibrary { Id = libB, Name = "Kids", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/b" } });

        var a = AddShow(db, libA, "42", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        AddEpisode(db, libA, a, 1, 1, "/a/x/S01E01.mkv", "1080p");
        var b = AddShow(db, libB, "42", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        AddEpisode(db, libB, b, 1, 1, "/b/x/S01E01.mkv", "1080p");
        db.SaveChanges();

        var result = await new MediaDedupeRepository(db).MergeDuplicateTvShowsAsync(null);

        Assert.Equal(0, result.GroupsMerged);
        Assert.Equal(2, await db.Set<TvShow>().CountAsync());
    }
}
