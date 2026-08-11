using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryThumbnailCoverageTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("thumb-coverage-" + Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task Counts_episodes_of_a_tv_library_through_their_show()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var show = new TvShow { Id = Guid.NewGuid(), Title = "Doctor Who", LibraryId = libraryId };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, TvShow = show, LibraryId = libraryId };
        db.Set<TvShow>().Add(show);
        db.Set<Season>().Add(season);
        db.Set<Episode>().Add(new Episode { Id = Guid.NewGuid(), Title = "E1", EpisodeNumber = 1, SeasonId = season.Id, Season = season, LibraryId = libraryId, LastVideoThumbnailGenerationAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.Set<Episode>().Add(new Episode { Id = Guid.NewGuid(), Title = "E2", EpisodeNumber = 2, SeasonId = season.Id, Season = season, LibraryId = libraryId });
        db.Set<Episode>().Add(new Episode { Id = Guid.NewGuid(), Title = "E3", EpisodeNumber = 3, SeasonId = season.Id, Season = season, LibraryId = libraryId });
        await db.SaveChangesAsync();

        var (total, withThumbs) = await repo.GetVideoThumbnailCoverageAsync(libraryId);

        Assert.Equal(3, total);
        Assert.Equal(1, withThumbs);
    }

    [Fact]
    public async Task Counts_movies_of_a_movie_library()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        db.Set<Movie>().Add(new Movie { Id = Guid.NewGuid(), Title = "A", LibraryId = libraryId, LastVideoThumbnailGenerationAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.Set<Movie>().Add(new Movie { Id = Guid.NewGuid(), Title = "B", LibraryId = libraryId });
        await db.SaveChangesAsync();

        var (total, withThumbs) = await repo.GetVideoThumbnailCoverageAsync(libraryId);

        Assert.Equal(2, total);
        Assert.Equal(1, withThumbs);
    }

    [Fact]
    public async Task Does_not_count_items_from_another_library()
    {
        using var db = NewContext();
        var libraryA = Guid.NewGuid();
        var libraryB = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var show = new TvShow { Id = Guid.NewGuid(), Title = "Show", LibraryId = libraryA };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, TvShow = show, LibraryId = libraryA };
        db.Set<TvShow>().Add(show);
        db.Set<Season>().Add(season);
        db.Set<Episode>().Add(new Episode { Id = Guid.NewGuid(), Title = "E1", EpisodeNumber = 1, SeasonId = season.Id, Season = season, LibraryId = libraryA });
        await db.SaveChangesAsync();

        var (total, _) = await repo.GetVideoThumbnailCoverageAsync(libraryB);

        Assert.Equal(0, total);
    }
}
