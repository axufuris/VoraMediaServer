using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryPreservationTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("preservation-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static (Guid LibraryId, Guid ProfileId) Seed(VoraDbContext db)
    {
        var libraryId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        });
        db.Set<UserProfile>().Add(new UserProfile { Id = profileId, Name = "Test", UserId = Guid.NewGuid() });
        db.SaveChanges();
        return (libraryId, profileId);
    }

    [Fact]
    public async Task DeleteThenReAdd_RestoresRatingAndWatchState_ByExternalId()
    {
        using var db = NewContext();
        var (libraryId, profileId) = Seed(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var movie = new Movie { Title = "The Matrix", LibraryId = libraryId, TmdbId = "603" };
        await repo.AddMediaItemAsync(movie);

        db.UserMediaRatings.Add(new UserMediaRating { ProfileId = profileId, MediaItemId = movie.Id, Rating = 9m, RatedAt = DateTime.UtcNow });
        db.UserMediaStates.Add(new UserMediaState { ProfileId = profileId, MediaItemId = movie.Id, ResumePositionSeconds = 1234, IsPlayed = false, LastPlayedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await repo.DeleteMediaItemAsync(movie.Id);

        Assert.Equal(1, await db.PreservedUserMediaData.CountAsync());

        var readded = new Movie { Title = "The Matrix", LibraryId = libraryId, TmdbId = "603" };
        await repo.AddMediaItemAsync(readded);

        var rating = await db.UserMediaRatings.FirstOrDefaultAsync(r => r.MediaItemId == readded.Id && r.ProfileId == profileId);
        var state = await db.UserMediaStates.FirstOrDefaultAsync(s => s.MediaItemId == readded.Id && s.ProfileId == profileId);

        Assert.NotNull(rating);
        Assert.Equal(9m, rating!.Rating);
        Assert.NotNull(state);
        Assert.Equal(1234, state!.ResumePositionSeconds);
        Assert.Equal(0, await db.PreservedUserMediaData.CountAsync());
    }

    [Fact]
    public async Task ReAdd_WithDifferentExternalId_DoesNotRestore()
    {
        using var db = NewContext();
        var (libraryId, profileId) = Seed(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var movie = new Movie { Title = "The Matrix", LibraryId = libraryId, TmdbId = "603" };
        await repo.AddMediaItemAsync(movie);
        db.UserMediaRatings.Add(new UserMediaRating { ProfileId = profileId, MediaItemId = movie.Id, Rating = 9m, RatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await repo.DeleteMediaItemAsync(movie.Id);

        var unrelated = new Movie { Title = "Other", LibraryId = libraryId, TmdbId = "999" };
        await repo.AddMediaItemAsync(unrelated);

        Assert.False(await db.UserMediaRatings.AnyAsync(r => r.MediaItemId == unrelated.Id));
        Assert.Equal(1, await db.PreservedUserMediaData.CountAsync());
    }

    [Fact]
    public async Task SyncItemEditionFromParts_UsesBestResolutionPartsEdition()
    {
        using var db = NewContext();
        var (libraryId, _) = Seed(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var movie = new Movie { Title = "Blade Runner", LibraryId = libraryId, TmdbId = "78" };
        await repo.AddMediaItemAsync(movie);

        db.MediaParts.Add(new MediaPart { FilePath = "/m/br-1080-dc.mkv", MediaItemId = movie.Id, Resolution = "1080p", Edition = "Director's Cut" });
        db.MediaParts.Add(new MediaPart { FilePath = "/m/br-2160-theatrical.mkv", MediaItemId = movie.Id, Resolution = "2160p", Edition = null });
        await db.SaveChangesAsync();

        await repo.SyncItemEditionFromPartsAsync(movie.Id);

        var refreshed = await db.MediaItems.FindAsync(movie.Id);
        Assert.Null(refreshed!.Edition); // best (2160p) part has no edition
    }

    [Fact]
    public async Task SyncItemEditionFromParts_TakesEditionOfHighestResolutionPart()
    {
        using var db = NewContext();
        var (libraryId, _) = Seed(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var movie = new Movie { Title = "Dune", LibraryId = libraryId, TmdbId = "438631" };
        await repo.AddMediaItemAsync(movie);

        db.MediaParts.Add(new MediaPart { FilePath = "/m/dune-1080.mkv", MediaItemId = movie.Id, Resolution = "1080p", Edition = null });
        db.MediaParts.Add(new MediaPart { FilePath = "/m/dune-2160-imax.mkv", MediaItemId = movie.Id, Resolution = "2160p", Edition = "IMAX" });
        await db.SaveChangesAsync();

        await repo.SyncItemEditionFromPartsAsync(movie.Id);

        var refreshed = await db.MediaItems.FindAsync(movie.Id);
        Assert.Equal("IMAX", refreshed!.Edition);
    }

    [Fact]
    public async Task DeleteMovie_WithoutExternalId_DoesNotArchive()
    {
        using var db = NewContext();
        var (libraryId, profileId) = Seed(db);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var movie = new Movie { Title = "Home Video", LibraryId = libraryId };
        await repo.AddMediaItemAsync(movie);
        db.UserMediaRatings.Add(new UserMediaRating { ProfileId = profileId, MediaItemId = movie.Id, Rating = 7m, RatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await repo.DeleteMediaItemAsync(movie.Id);

        Assert.Equal(0, await db.PreservedUserMediaData.CountAsync());
    }
}
