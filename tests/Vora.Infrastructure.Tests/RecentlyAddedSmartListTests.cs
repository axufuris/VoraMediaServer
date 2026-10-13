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

public sealed class RecentlyAddedShowsWorld
{
    public static readonly DateTime ShowAdded = new(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime EpisodeAdded = new(2026, 10, 4, 23, 59, 0, DateTimeKind.Utc);
    public static readonly DateTime NewShowAdded = new(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc);

    public Guid LibraryId { get; } = Guid.NewGuid();
    public Guid NewEpisodeId { get; private set; }
    public Guid NewShowId { get; private set; }
    public Guid MovieId { get; private set; }

    public static readonly SmartListRulesDto MoviesAndShows = new() { MediaTypes = ["Movie", "TvShow", "Season", "Episode"] };

    public async Task SeedAsync(VoraDbContext db)
    {
        db.Add(new MediaLibrary { Id = LibraryId, Name = "Mixed " + LibraryId.ToString("N")[..6], Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } });

        var marshals = new TvShow { Title = "Marshals", LibraryId = LibraryId, AddedAt = ShowAdded, LastContentAddedAt = EpisodeAdded.AddHours(2) };
        var seasonOne = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = marshals.Id, LibraryId = LibraryId, AddedAt = ShowAdded, LastContentAddedAt = ShowAdded };
        var seasonTwo = new Season { Title = "Season 2", SeasonNumber = 2, TvShowId = marshals.Id, LibraryId = LibraryId, AddedAt = EpisodeAdded, LastContentAddedAt = EpisodeAdded.AddHours(2) };
        var oldEpisode = new Episode { Title = "Piya Wiconi", EpisodeNumber = 1, SeasonId = seasonOne.Id, LibraryId = LibraryId, AddedAt = ShowAdded };
        var newEpisode = new Episode { Title = "All Hat No Cattle", EpisodeNumber = 1, SeasonId = seasonTwo.Id, LibraryId = LibraryId, AddedAt = EpisodeAdded };

        var from = new TvShow { Title = "FROM", LibraryId = LibraryId, AddedAt = NewShowAdded, LastContentAddedAt = NewShowAdded.AddHours(1) };
        var fromSeason = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = from.Id, LibraryId = LibraryId, AddedAt = NewShowAdded, LastContentAddedAt = NewShowAdded.AddHours(1) };
        var fromPilot = new Episode { Title = "Long Day's Journey", EpisodeNumber = 1, SeasonId = fromSeason.Id, LibraryId = LibraryId, AddedAt = NewShowAdded };
        var fromSecond = new Episode { Title = "The Way Things Are Now", EpisodeNumber = 2, SeasonId = fromSeason.Id, LibraryId = LibraryId, AddedAt = NewShowAdded.AddHours(1) };

        var movie = new Movie { Title = "Moana", LibraryId = LibraryId, AddedAt = EpisodeAdded.AddHours(1) };

        db.AddRange(marshals, seasonOne, seasonTwo, oldEpisode, newEpisode, from, fromSeason, fromPilot, fromSecond, movie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        NewEpisodeId = newEpisode.Id;
        NewShowId = from.Id;
        MovieId = movie.Id;
    }

    public const int WholeShowEpisodes = 230;
    public static readonly DateTime WholeShowAdded = new(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc);
    public Guid WholeShowId { get; private set; }

    public async Task SeedWholeShowImportAsync(VoraDbContext db)
    {
        var lastEpisodeAdded = WholeShowAdded.AddSeconds(WholeShowEpisodes);
        var topGear = new TvShow { Title = "Top Gear", LibraryId = LibraryId, AddedAt = WholeShowAdded, LastContentAddedAt = lastEpisodeAdded };
        db.Add(topGear);

        var episodeNumber = 0;
        for (var seasonNumber = 1; episodeNumber < WholeShowEpisodes; seasonNumber++)
        {
            var season = new Season { Title = "Season " + seasonNumber, SeasonNumber = seasonNumber, TvShowId = topGear.Id, LibraryId = LibraryId, AddedAt = WholeShowAdded.AddSeconds(episodeNumber + 1) };
            db.Add(season);
            for (var inSeason = 1; inSeason <= 10 && episodeNumber < WholeShowEpisodes; inSeason++)
            {
                episodeNumber++;
                season.LastContentAddedAt = WholeShowAdded.AddSeconds(episodeNumber);
                db.Add(new Episode { Title = "Episode " + inSeason, EpisodeNumber = inSeason, SeasonId = season.Id, LibraryId = LibraryId, AddedAt = WholeShowAdded.AddSeconds(episodeNumber) });
            }
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        WholeShowId = topGear.Id;
    }

    public Task<List<LibraryItemVM>> RecentlyAddedAsync(VoraDbContext db, SmartListRulesDto rules) =>
        new SmartListRepository(db).GetSmartListItemsAsync(null, LibraryId, rules, SmartListSortBy.DateAddedDesc, 20);
}

public class RecentlyAddedSmartListTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>().UseInMemoryDatabase("recently-added-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task Each_show_appears_once_as_what_is_new_about_it()
    {
        await using var db = NewContext();
        var world = new RecentlyAddedShowsWorld();
        await world.SeedAsync(db);

        var items = await world.RecentlyAddedAsync(db, RecentlyAddedShowsWorld.MoviesAndShows);

        items.Select(i => i.Id).Should().Equal(world.NewShowId, world.MovieId, world.NewEpisodeId);
    }

    [Fact]
    public async Task A_whole_show_added_at_once_is_one_tile_and_leaves_room_for_the_others()
    {
        await using var db = NewContext();
        var world = new RecentlyAddedShowsWorld();
        await world.SeedAsync(db);
        await world.SeedWholeShowImportAsync(db);

        var items = await world.RecentlyAddedAsync(db, RecentlyAddedShowsWorld.MoviesAndShows);

        items.Select(i => i.Id).Should().Equal(world.WholeShowId, world.NewShowId, world.MovieId, world.NewEpisodeId);
    }

    [Fact]
    public async Task A_list_of_shows_alone_still_brings_a_show_up_when_it_gets_a_new_episode()
    {
        await using var db = NewContext();
        var world = new RecentlyAddedShowsWorld();
        await world.SeedAsync(db);

        var items = await world.RecentlyAddedAsync(db, new SmartListRulesDto { MediaTypes = ["TvShow"] });

        items.Select(i => i.Title).Should().Equal("FROM", "Marshals");
    }
}
