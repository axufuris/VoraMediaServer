using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public sealed class UnpinnedLibrariesWorld
{
    private static readonly DateTime Day = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public Guid Movies { get; } = Guid.NewGuid();
    public Guid Shows { get; } = Guid.NewGuid();
    public Guid TestMovies { get; } = Guid.NewGuid();
    public Guid TestShows { get; } = Guid.NewGuid();

    public List<Guid> Unpinned => [TestMovies, TestShows];

    public static readonly SmartListRulesDto MoviesAndShows = new() { MediaTypes = ["Movie", "TvShow", "Season", "Episode"] };

    public async Task SeedAsync(VoraDbContext db)
    {
        var tag = Movies.ToString("N")[..6];
        db.AddRange(
            new MediaLibrary { Id = Movies, Name = "Movies " + tag, Type = LibraryType.Movie, FolderPaths = new List<string> { "/movies/" + tag } },
            new MediaLibrary { Id = Shows, Name = "Shows " + tag, Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv/" + tag } },
            new MediaLibrary { Id = TestMovies, Name = "Test Movies " + tag, Type = LibraryType.Movie, FolderPaths = new List<string> { "/test-movies/" + tag } },
            new MediaLibrary { Id = TestShows, Name = "Test Shows " + tag, Type = LibraryType.TvShow, FolderPaths = new List<string> { "/test-tv/" + tag } });

        Film(db, Movies, "Heat", 1);
        Film(db, Movies, "Alien", 2);
        Film(db, Movies, "Arrival", 3);
        Series(db, Shows, "The Office", 4);
        Series(db, Shows, "Parks and Recreation", 5);
        Film(db, TestMovies, "Test Film A", 10);
        Film(db, TestMovies, "Test Film B", 11);
        Series(db, TestShows, "Seinfeld", 12);
        Series(db, TestShows, "Raising Hope", 13);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void Film(VoraDbContext db, Guid libraryId, string title, int day) =>
        db.Add(new Movie { Title = title, LibraryId = libraryId, AddedAt = Day.AddDays(day) });

    private static void Series(VoraDbContext db, Guid libraryId, string title, int day)
    {
        var added = Day.AddDays(day);
        var show = new TvShow { Title = title, LibraryId = libraryId, AddedAt = added, LastContentAddedAt = added.AddMinutes(3) };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = libraryId, AddedAt = added, LastContentAddedAt = added.AddMinutes(3) };
        db.AddRange(show, season);
        for (var number = 1; number <= 3; number++)
        {
            db.Add(new Episode { Title = $"{title} {number}", EpisodeNumber = number, SeasonId = season.Id, LibraryId = libraryId, AddedAt = added.AddMinutes(number) });
        }
    }

    public Task<List<LibraryItemVM>> RowAsync(VoraDbContext db, SmartListRulesDto? rules, int maxItems, List<Guid>? excluded = null) =>
        new SmartListRepository(db).GetSmartListItemsAsync(null, null, rules, SmartListSortBy.DateAddedDesc, maxItems, excludedLibs: excluded);
}

public class UnpinnedLibrarySmartListTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase("unpinned-" + Guid.NewGuid().ToString("N")).Options);

    private static async Task<(UnpinnedLibrariesWorld World, VoraDbContext Db)> WorldAsync()
    {
        var db = NewContext();
        var world = new UnpinnedLibrariesWorld();
        await world.SeedAsync(db);
        return (world, db);
    }

    [Fact]
    public async Task Recently_added_movies_and_shows_leaves_out_unpinned_libraries_and_still_fills()
    {
        var (world, db) = await WorldAsync();
        await using var _ = db;

        var everything = await world.RowAsync(db, UnpinnedLibrariesWorld.MoviesAndShows, 4);
        var pinnedOnly = await world.RowAsync(db, UnpinnedLibrariesWorld.MoviesAndShows, 4, world.Unpinned);

        everything.Select(i => i.Title).Should().Equal("Raising Hope", "Seinfeld", "Test Film B", "Test Film A");
        pinnedOnly.Select(i => i.Title).Should().Equal("Parks and Recreation", "The Office", "Arrival", "Alien");
    }

    [Fact]
    public async Task A_movie_row_fills_from_the_libraries_still_pinned()
    {
        var (world, db) = await WorldAsync();
        await using var _ = db;

        var row = await world.RowAsync(db, new SmartListRulesDto { MediaTypes = ["Movie"] }, 2, world.Unpinned);

        row.Select(i => i.Title).Should().Equal("Arrival", "Alien");
    }

    [Fact]
    public async Task An_episode_row_takes_its_one_episode_per_show_from_the_shows_still_pinned()
    {
        var (world, db) = await WorldAsync();
        await using var _ = db;

        var row = await world.RowAsync(db, new SmartListRulesDto { MediaTypes = ["Episode"] }, 2, world.Unpinned);

        row.Select(i => i.Title).Should().Equal("Parks and Recreation 1", "The Office 1");
    }

    [Fact]
    public async Task A_row_on_the_default_rules_leaves_out_a_single_unpinned_library()
    {
        var (world, db) = await WorldAsync();
        await using var _ = db;

        var row = await world.RowAsync(db, null, 5, [world.TestShows]);

        row.Select(i => i.Title).Should().Equal("Test Film B", "Test Film A", "Parks and Recreation", "The Office", "Arrival");
    }

    [Fact]
    public async Task Leaving_nothing_out_reads_every_library()
    {
        var (world, db) = await WorldAsync();
        await using var _ = db;

        var row = await world.RowAsync(db, new SmartListRulesDto { MediaTypes = ["Movie"] }, 5, []);

        row.Select(i => i.Title).Should().Equal("Test Film B", "Test Film A", "Arrival", "Alien", "Heat");
    }
}
