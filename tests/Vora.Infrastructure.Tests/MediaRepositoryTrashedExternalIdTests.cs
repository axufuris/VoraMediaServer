using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Deleting a video soft-deletes it: the row stays with MissingSince stamped so
// Trash can offer it back until the purge. Every "does the library have this?"
// lookup therefore has to ask for a live copy, or a title sitting in Trash keeps
// answering yes — Discovery badges it In Library, its tile routes to an item the
// client hides, and a request to re-acquire it is dropped as redundant.
public class MediaRepositoryTrashedExternalIdTests
{
    private const string Tmdb = "1368337";

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("trashed-external-id-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid AddMovie(VoraDbContext db, string tmdbId, DateTime? missingSince)
    {
        var movie = new Movie
        {
            Id = Guid.NewGuid(),
            Title = "The Odyssey",
            TmdbId = tmdbId,
            LibraryId = Guid.NewGuid(),
            MissingSince = missingSince,
        };
        db.Set<Movie>().Add(movie);
        db.SaveChanges();
        return movie.Id;
    }

    private static MediaRepository Repo(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private static readonly DateTime Trashed = new(2026, 8, 27, 20, 35, 50, DateTimeKind.Utc);

    [Fact]
    public async Task A_trashed_movie_does_not_resolve_to_a_local_id()
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, Trashed);

        (await Repo(db).GetLocalIdsByExternalIdsAsync([Tmdb], "Movie")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_live_movie_still_resolves_to_its_local_id()
    {
        using var db = NewContext();
        var id = AddMovie(db, Tmdb, missingSince: null);

        var found = await Repo(db).GetLocalIdsByExternalIdsAsync([Tmdb], "Movie");

        found.Should().ContainKey(Tmdb).WhoseValue.Should().Be(id);
    }

    // Restoring from Trash clears MissingSince rather than re-adding the row, so
    // the badge has to come back on its own.
    [Fact]
    public async Task Restoring_from_trash_makes_it_resolve_again()
    {
        using var db = NewContext();
        var id = AddMovie(db, Tmdb, Trashed);

        db.Set<Movie>().Single(m => m.Id == id).MissingSince = null;
        db.SaveChanges();

        (await Repo(db).GetLocalIdsByExternalIdsAsync([Tmdb], "Movie")).Should().ContainKey(Tmdb);
    }

    // A re-added copy lands as a second row while the old one waits for purge,
    // and the live one is the answer.
    [Fact]
    public async Task A_live_copy_wins_over_a_trashed_row_with_the_same_external_id()
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, Trashed);
        var liveId = AddMovie(db, Tmdb, missingSince: null);

        var found = await Repo(db).GetLocalIdsByExternalIdsAsync([Tmdb], "Movie");

        found.Should().ContainKey(Tmdb).WhoseValue.Should().Be(liveId);
    }

    // The two lookups back the same badge from different call paths — Discovery
    // rows go through one and the details page through the other — so they must
    // never disagree about whether a title is held.
    [Theory]
    [InlineData("Movie")]
    [InlineData("")]
    public async Task Both_external_id_lookups_agree_that_a_trashed_title_is_absent(string type)
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, Trashed);
        var repo = Repo(db);

        (await repo.GetExistingExternalIdsAsync([Tmdb], type)).Should().BeEmpty();
        (await repo.GetLocalIdsByExternalIdsAsync([Tmdb], type)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_trashed_title_does_not_count_as_already_owned()
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, Trashed);

        (await Repo(db).MediaExistsByExternalIdAsync(Tmdb, "Movie")).Should().BeFalse();
    }

    [Fact]
    public async Task A_live_title_still_counts_as_already_owned()
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, missingSince: null);

        (await Repo(db).MediaExistsByExternalIdAsync(Tmdb, "Movie")).Should().BeTrue();
    }

    // The untyped branch is the fallback for anything that isn't Movie/TvShow.
    [Fact]
    public async Task The_untyped_lookup_also_ignores_a_trashed_title()
    {
        using var db = NewContext();
        AddMovie(db, Tmdb, Trashed);

        (await Repo(db).MediaExistsByExternalIdAsync(Tmdb, "Anything")).Should().BeFalse();
    }
}
