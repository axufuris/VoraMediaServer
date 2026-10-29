using Microsoft.EntityFrameworkCore;
using Vora.Application.Collections;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class ThemeCollectionStorageTests
{
    private readonly Guid _movies = Guid.NewGuid();
    private readonly Guid _shows = Guid.NewGuid();

    private static VoraDbContext InMemory() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("theme-collections-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static VoraDbContext Postgres() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseNpgsql("Host=localhost;Database=sql-only", npgsql => npgsql.UseVector())
            .Options);

    private async Task<VoraDbContext> LibrariesAsync()
    {
        var db = InMemory();
        db.AddRange(
            new MediaLibrary { Id = _movies, Name = "Movies", Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies" } },
            new MediaLibrary { Id = _shows, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return db;
    }

    [Fact]
    public async Task A_new_theme_forgets_the_answers_given_for_the_old_one_and_the_same_theme_keeps_them()
    {
        await using var db = await LibrariesAsync();
        var collection = new Collection { Title = "Holidays" };
        var movie = new Movie { Title = "Elf", LibraryId = _movies };
        db.AddRange(collection, movie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var themes = new CollectionThemeRepository(db);

        await themes.SaveAsync(new CollectionTheme { CollectionId = collection.Id, Theme = "Christmas", Profile = "Films set at Christmas.", Keywords = new() { "christmas" } }, forgetVerdicts: true);
        await themes.SaveVerdictsAsync(collection.Id, new Dictionary<Guid, bool> { [movie.Id] = true });
        await themes.SaveAsync(new CollectionTheme { CollectionId = collection.Id, Theme = "Christmas", Profile = "Films set at Christmas.", Keywords = new() { "christmas" }, Embedding = new[] { 0.5f }, EmbeddingModel = "text-embedding-3-small" }, forgetVerdicts: false);

        (await themes.GetVerdictsAsync(collection.Id)).Should().ContainKey(movie.Id);
        (await themes.GetAsync(collection.Id))?.EmbeddingModel.Should().Be("text-embedding-3-small");

        await themes.SaveAsync(new CollectionTheme { CollectionId = collection.Id, Theme = "Thanksgiving", Profile = "Films set at Thanksgiving.", Keywords = new() { "thanksgiving" } }, forgetVerdicts: true);

        (await themes.GetVerdictsAsync(collection.Id)).Should().BeEmpty();
        (await themes.GetAsync(collection.Id))?.Theme.Should().Be("Thanksgiving");
    }

    [Fact]
    public async Task A_title_answered_again_keeps_one_answer()
    {
        await using var db = await LibrariesAsync();
        var collection = new Collection { Title = "Halloween" };
        var movie = new Movie { Title = "Beetlejuice", LibraryId = _movies };
        db.AddRange(collection, movie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var themes = new CollectionThemeRepository(db);

        await themes.SaveVerdictsAsync(collection.Id, new Dictionary<Guid, bool> { [movie.Id] = false });
        await themes.SaveVerdictsAsync(collection.Id, new Dictionary<Guid, bool> { [movie.Id] = true });

        (await themes.GetVerdictsAsync(collection.Id)).Should().Equal(new Dictionary<Guid, bool> { [movie.Id] = true });
    }

    [Fact]
    public async Task A_candidate_is_described_by_its_title_year_kind_and_genres()
    {
        await using var db = await LibrariesAsync();
        var show = new TvShow { Title = "Goosebumps", LibraryId = _shows, ReleaseDate = new DateOnly(1995, 10, 27), Overview = "Kids face monsters." };
        show.Genres.Add(new Genre { Name = "Family" });
        db.Add(show);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var candidate = (await new CollectionThemeRepository(db).GetCandidatesAsync(new[] { show.Id })).Single();

        candidate.Should().BeEquivalentTo(new ThemeCandidate(show.Id, "Goosebumps", 1995, true, "Kids face monsters.", new List<string> { "Family" }));
    }

    [Fact]
    public void The_closest_titles_are_ordered_by_the_indexed_distance_of_the_current_model_s_vectors()
    {
        using var db = Postgres();

        var sql = CollectionThemeRepository.NearestTitles(db, new[] { 0.1f, 0.2f }, "text-embedding-3-small", Guid.NewGuid(), 150).ToQueryString();

        sql.Should().Contain("ORDER BY m.\"Embedding\" <=> @");
        sql.Should().Contain("\"Model\" = @");
        sql.Should().Contain("\"MissingSince\" IS NULL");
    }

    [Fact]
    public void The_keyword_search_and_the_season_filter_run_in_the_database()
    {
        using var db = Postgres();
        var wanted = new List<string> { "halloween" };

        db.MediaItems.Where(m => m.Keywords != null && wanted.Any(k => m.Keywords.Contains(k))).ToQueryString().Should().Contain("&&");
        db.Collections.InSeason(new DateOnly(2026, 10, 15)).ToQueryString().Should().Contain("\"VisibleEveryYear\"");
    }

    [Theory]
    [InlineData(2030, 10, 15, true)]
    [InlineData(2030, 11, 2, false)]
    public async Task A_home_row_from_a_collection_that_repeats_every_year_shows_only_in_its_season(int year, int month, int day, bool shown)
    {
        await using var db = await LibrariesAsync();
        var collection = new Collection
        {
            Title = "Halloween",
            VisibleStartDate = new DateTime(2000, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            VisibleEndDate = new DateTime(2000, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            VisibleEveryYear = true,
        };
        db.AddRange(collection, new SmartList { Title = "Halloween", CollectionId = collection.Id }, new SmartList { Title = "Recently Added" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rows = await new SmartListRepository(db).GetActiveClientListsAsync(false, new DateOnly(year, month, day));

        rows.Select(r => r.Title).Should().BeEquivalentTo(shown ? new[] { "Halloween", "Recently Added" } : new[] { "Recently Added" });
    }

    [Fact]
    public async Task A_home_row_from_a_collection_also_keeps_its_own_window()
    {
        await using var db = await LibrariesAsync();
        var collection = new Collection { Title = "Christmas" };
        db.AddRange(collection, new SmartList { Title = "Christmas", CollectionId = collection.Id, ActiveStartMonth = 12, ActiveStartDay = 1, ActiveEndMonth = 12, ActiveEndDay = 26 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new SmartListRepository(db);

        (await repository.GetActiveClientListsAsync(false, new DateOnly(2026, 12, 10))).Should().ContainSingle();
        (await repository.GetActiveClientListsAsync(false, new DateOnly(2026, 7, 4))).Should().BeEmpty();
    }
}
