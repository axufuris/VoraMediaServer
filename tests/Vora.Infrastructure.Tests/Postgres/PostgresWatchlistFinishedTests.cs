using Vora.Application.Watchlist;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// What takes a finished title off the watchlist, run on the real database: which
// title an episode belongs to, and whether a profile has watched all of it.
public class PostgresWatchlistFinishedTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    [Fact]
    public async Task A_title_is_found_from_any_of_its_items_and_finished_when_all_of_it_was_watched()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var cancellationToken = TestContext.Current.CancellationToken;
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var db = database.NewContext();

        var library = new MediaLibrary { Name = "Library " + tag, Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv/" + tag } };
        var movie = new Movie { Title = "The Matrix " + tag, LibraryId = library.Id };
        var show = new TvShow { Title = "Breaking Bad " + tag, LibraryId = library.Id };
        var season = new Season { Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id };
        var episodes = Enumerable.Range(1, 3)
            .Select(n => new Episode { Title = $"Episode {n}", EpisodeNumber = n, SeasonId = season.Id, LibraryId = library.Id })
            .ToList();
        var account = new User { Email = $"{tag}@example.com", DisplayName = "Andy" };
        var mine = new UserProfile { Name = "Andy " + tag, UserId = account.Id };
        var someoneElse = new UserProfile { Name = "Kids " + tag, UserId = account.Id };
        var profile = mine.Id;
        db.AddRange(library, movie, show, season, account, mine, someoneElse);
        db.AddRange(episodes);
        await db.SaveChangesAsync(cancellationToken);
        var repository = new WatchlistRepository(db);

        (await repository.GetTitleAsync(episodes[1].Id)).Should().Be(new WatchlistTitle(show.Id, "TvShow"));
        (await repository.GetTitleAsync(season.Id)).Should().Be(new WatchlistTitle(show.Id, "TvShow"));
        (await repository.GetTitleAsync(show.Id)).Should().Be(new WatchlistTitle(show.Id, "TvShow"));
        (await repository.GetTitleAsync(movie.Id)).Should().Be(new WatchlistTitle(movie.Id, "Movie"));
        (await repository.GetTitleAsync(Guid.NewGuid())).Should().BeNull();

        db.UserMediaStates.AddRange(episodes.Take(2).Select(e => new UserMediaState { ProfileId = profile, MediaItemId = e.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow }));
        await db.SaveChangesAsync(cancellationToken);
        (await repository.IsFinishedAsync(profile, show.Id)).Should().BeFalse();
        (await repository.IsFinishedAsync(profile, movie.Id)).Should().BeFalse();

        db.UserMediaStates.AddRange(
            new UserMediaState { ProfileId = profile, MediaItemId = episodes[2].Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow },
            new UserMediaState { ProfileId = profile, MediaItemId = movie.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        (await repository.IsFinishedAsync(profile, show.Id)).Should().BeTrue();
        (await repository.IsFinishedAsync(profile, movie.Id)).Should().BeTrue();
        (await repository.IsFinishedAsync(someoneElse.Id, show.Id)).Should().BeFalse();
    }
}
