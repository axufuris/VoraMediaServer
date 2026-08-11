using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryTvShowMatchTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("tvshow-match-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid AddShow(VoraDbContext db, Guid libraryId, string title, int year)
    {
        var show = new TvShow
        {
            Id = Guid.NewGuid(),
            Title = title,
            LibraryId = libraryId,
            ReleaseDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        db.Set<TvShow>().Add(show);
        db.SaveChanges();
        return show.Id;
    }

    [Fact]
    public async Task Matches_a_show_whose_stored_title_gained_a_year_suffix()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
        var showId = AddShow(db, libraryId, "Doctor Who (2005)", 2005);

        var match = await repo.GetTvShowIdByTitleAndYearAsync("Doctor Who", 2005, libraryId);

        Assert.Equal(showId, match);
    }

    [Fact]
    public async Task Disambiguates_two_same_titled_shows_by_year()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
        var classicId = AddShow(db, libraryId, "Doctor Who (2005)", 2005);
        var revivalId = AddShow(db, libraryId, "Doctor Who (2023)", 2023);

        Assert.Equal(classicId, await repo.GetTvShowIdByTitleAndYearAsync("Doctor Who", 2005, libraryId));
        Assert.Equal(revivalId, await repo.GetTvShowIdByTitleAndYearAsync("Doctor Who", 2023, libraryId));
    }

    [Fact]
    public async Task Returns_null_when_the_title_is_ambiguous_and_no_year_is_given()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
        AddShow(db, libraryId, "Doctor Who (2005)", 2005);
        AddShow(db, libraryId, "Doctor Who (2023)", 2023);

        Assert.Null(await repo.GetTvShowIdByTitleAndYearAsync("Doctor Who", null, libraryId));
    }

    [Fact]
    public async Task Does_not_match_a_show_in_a_different_library()
    {
        using var db = NewContext();
        var libraryA = Guid.NewGuid();
        var libraryB = Guid.NewGuid();
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);
        AddShow(db, libraryA, "Doctor Who (2005)", 2005);

        Assert.Null(await repo.GetTvShowIdByTitleAndYearAsync("Doctor Who", 2005, libraryB));
    }
}
