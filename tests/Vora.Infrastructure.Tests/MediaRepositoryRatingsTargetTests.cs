using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryRatingsTargetTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("ratings-target-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedLibrary(VoraDbContext db, string? rating1Provider, string? rating2Provider)
    {
        var id = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = id,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" },
            ThirdPartyRating1ProviderId = rating1Provider,
            ThirdPartyRating2ProviderId = rating2Provider
        });
        return id;
    }

    private static Movie AddMovie(VoraDbContext db, Guid libraryId, string title, decimal? r1, decimal? r2)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = title, LibraryId = libraryId, ThirdPartyRating1 = r1, ThirdPartyRating2 = r2 };
        db.Set<Movie>().Add(movie);
        return movie;
    }

    [Fact]
    public async Task Targets_items_missing_the_second_rating_when_a_second_provider_is_configured()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, "tmdb_rating", "omdb_rotten_tomatoes");
        var bothSet = AddMovie(db, libraryId, "Both", 7m, 80m);
        var missingSecond = AddMovie(db, libraryId, "MissingRT", 7m, null);
        var missingFirst = AddMovie(db, libraryId, "MissingTmdb", null, 80m);
        await db.SaveChangesAsync();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var ids = (await repo.GetMediaIdsMissingRatingsAsync(libraryId)).ToHashSet();

        Assert.Contains(missingSecond.Id, ids);
        Assert.Contains(missingFirst.Id, ids);
        Assert.DoesNotContain(bothSet.Id, ids);
    }

    [Fact]
    public async Task Ignores_a_missing_second_rating_when_no_second_provider_is_configured()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, "tmdb_rating", null);
        var missingSecond = AddMovie(db, libraryId, "MissingRT", 7m, null);
        var missingFirst = AddMovie(db, libraryId, "MissingTmdb", null, null);
        await db.SaveChangesAsync();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var ids = (await repo.GetMediaIdsMissingRatingsAsync(libraryId)).ToHashSet();

        Assert.DoesNotContain(missingSecond.Id, ids);
        Assert.Contains(missingFirst.Id, ids);
    }

    [Fact]
    public async Task Returns_empty_when_the_library_configures_no_rating_providers()
    {
        using var db = NewContext();
        var libraryId = SeedLibrary(db, null, "");
        AddMovie(db, libraryId, "Unrated", null, null);
        await db.SaveChangesAsync();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        Assert.Empty(await repo.GetMediaIdsMissingRatingsAsync(libraryId));
    }
}
