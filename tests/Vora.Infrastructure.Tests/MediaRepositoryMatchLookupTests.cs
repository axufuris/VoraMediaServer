using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Media.Dtos;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class MediaRepositoryMatchLookupTests
{
    private static readonly Guid LibraryId = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("match-lookup-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaRepository Repo(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private static TvShow Show(string title, string? tmdb = null, string? imdb = null, string? tvdb = null, Guid? libraryId = null, DateTime? missingSince = null, DateTime? addedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        LibraryId = libraryId ?? LibraryId,
        TmdbId = tmdb,
        ImdbId = imdb,
        TvdbId = tvdb,
        MissingSince = missingSince,
        AddedAt = addedAt ?? new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
    };

    [Theory]
    [InlineData(MediaMatchIds.Tmdb, "194583")]
    [InlineData(MediaMatchIds.Imdb, "tt18546730")]
    [InlineData(MediaMatchIds.Tvdb, "417549")]
    public async Task Another_show_with_the_chosen_id_is_found_by_any_source(string source, string id)
    {
        using var db = NewContext();
        var existing = Show("The Walking Dead: Dead City", "194583", "tt18546730", "417549");
        var duplicate = Show("The Walking Dead - Dead City [imdb-]");
        db.AddRange(existing, duplicate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, duplicate.Id, true, source, id);

        found.Should().Be(new MatchedItemIds(existing.Id, "194583", "tt18546730", "417549"));
    }

    [Fact]
    public async Task The_item_being_matched_is_not_found_as_its_own_duplicate()
    {
        using var db = NewContext();
        var show = Show("The Walking Dead: Dead City", tvdb: "417549");
        db.Add(show);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, show.Id, true, MediaMatchIds.Tvdb, "417549");

        found.Should().BeNull();
    }

    [Fact]
    public async Task A_show_in_another_library_is_not_a_duplicate()
    {
        using var db = NewContext();
        var elsewhere = Show("The Walking Dead: Dead City", tvdb: "417549", libraryId: Guid.NewGuid());
        var duplicate = Show("The Walking Dead - Dead City [imdb-]");
        db.AddRange(elsewhere, duplicate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, duplicate.Id, true, MediaMatchIds.Tvdb, "417549");

        found.Should().BeNull();
    }

    [Fact]
    public async Task A_trashed_show_is_not_a_merge_target()
    {
        using var db = NewContext();
        var trashed = Show("The Walking Dead: Dead City", tvdb: "417549", missingSince: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        var duplicate = Show("The Walking Dead - Dead City [imdb-]");
        db.AddRange(trashed, duplicate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, duplicate.Id, true, MediaMatchIds.Tvdb, "417549");

        found.Should().BeNull();
    }

    [Fact]
    public async Task A_movie_with_the_id_does_not_count_for_a_show()
    {
        using var db = NewContext();
        var movie = new Movie { Id = Guid.NewGuid(), Title = "Dead City", LibraryId = LibraryId, TmdbId = "194583" };
        var duplicate = Show("The Walking Dead - Dead City [imdb-]");
        db.AddRange(movie, duplicate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, duplicate.Id, true, MediaMatchIds.Tmdb, "194583");

        found.Should().BeNull();
    }

    [Fact]
    public async Task The_oldest_match_wins_when_several_share_the_id()
    {
        using var db = NewContext();
        var older = Show("Dead City", tvdb: "417549", addedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Show("Dead City", tvdb: "417549", addedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var duplicate = Show("The Walking Dead - Dead City [imdb-]");
        db.AddRange(newer, older, duplicate);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var found = await Repo(db).FindOtherItemByExternalIdAsync(LibraryId, duplicate.Id, true, MediaMatchIds.Tvdb, "417549");

        found!.Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task Existence_reflects_whether_the_row_is_still_there()
    {
        using var db = NewContext();
        var show = Show("Dead City");
        db.Add(show);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = Repo(db);

        (await repo.MediaItemExistsAsync(show.Id)).Should().BeTrue();
        (await repo.MediaItemExistsAsync(Guid.NewGuid())).Should().BeFalse();
    }
}
