using Microsoft.EntityFrameworkCore;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// What the watchlist needs to know to take a finished title off it.
public class WatchlistFinishedTests
{
    private readonly Guid _profile = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("watchlist-finished-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Library(Movie Movie, TvShow Show, Season Season, List<Episode> Episodes);

    private static async Task<Library> SeedAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Library", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var movie = new Movie { Id = Guid.NewGuid(), Title = "The Matrix", LibraryId = library.Id };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Breaking Bad", LibraryId = library.Id };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
        var episodes = Enumerable.Range(1, 3)
            .Select(n => new Episode { Id = Guid.NewGuid(), Title = $"Episode {n}", EpisodeNumber = n, SeasonId = season.Id, LibraryId = library.Id })
            .ToList();
        db.AddRange(library, movie, show, season);
        db.AddRange(episodes);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Library(movie, show, season, episodes);
    }

    private async Task WatchAsync(VoraDbContext db, params MediaItem[] items)
    {
        db.UserMediaStates.AddRange(items.Select(i => new UserMediaState { ProfileId = _profile, MediaItemId = i.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_episode_and_its_season_count_toward_their_show_and_a_film_toward_itself()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var repository = new WatchlistRepository(db);

        (await repository.GetTitleAsync(seeded.Episodes[1].Id)).Should().Be(new WatchlistTitle(seeded.Show.Id, "TvShow"));
        (await repository.GetTitleAsync(seeded.Season.Id)).Should().Be(new WatchlistTitle(seeded.Show.Id, "TvShow"));
        (await repository.GetTitleAsync(seeded.Show.Id)).Should().Be(new WatchlistTitle(seeded.Show.Id, "TvShow"));
        (await repository.GetTitleAsync(seeded.Movie.Id)).Should().Be(new WatchlistTitle(seeded.Movie.Id, "Movie"));
    }

    [Fact]
    public async Task Something_that_is_not_in_the_library_has_no_title()
    {
        await using var db = NewContext();
        await SeedAsync(db);

        (await new WatchlistRepository(db).GetTitleAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task A_film_is_finished_once_it_has_been_watched()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var repository = new WatchlistRepository(db);

        (await repository.IsFinishedAsync(_profile, seeded.Movie.Id)).Should().BeFalse();
        await WatchAsync(db, seeded.Movie);
        (await repository.IsFinishedAsync(_profile, seeded.Movie.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task A_show_is_finished_only_when_every_episode_has_been_watched()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var repository = new WatchlistRepository(db);

        await WatchAsync(db, seeded.Episodes[0], seeded.Episodes[1]);
        (await repository.IsFinishedAsync(_profile, seeded.Show.Id)).Should().BeFalse();

        await WatchAsync(db, seeded.Episodes[2]);
        (await repository.IsFinishedAsync(_profile, seeded.Show.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task An_episode_whose_file_has_gone_does_not_hold_a_show_back()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        await WatchAsync(db, seeded.Episodes[0], seeded.Episodes[1]);
        seeded.Episodes[2].MissingSince = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new WatchlistRepository(db).IsFinishedAsync(_profile, seeded.Show.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task What_another_profile_watched_does_not_finish_it_for_this_one()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        db.UserMediaStates.Add(new UserMediaState { ProfileId = Guid.NewGuid(), MediaItemId = seeded.Movie.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await new WatchlistRepository(db).IsFinishedAsync(_profile, seeded.Movie.Id)).Should().BeFalse();
    }
}
