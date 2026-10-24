using Microsoft.EntityFrameworkCore;
using Vora.Application.Libraries.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class LibraryItemWatchedStateTests
{
    private readonly Guid _profile = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("library-item-watched-state-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Shield(TvShow Show, Season SeasonSix, Season SeasonSeven, List<Episode> SixEpisodes, List<Episode> SevenEpisodes);

    private static async Task<Shield> SeedAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Agents of S.H.I.E.L.D.", LibraryId = library.Id };
        db.AddRange(library, show);

        (Season Season, List<Episode> Episodes) AddSeason(int number)
        {
            var season = new Season { Id = Guid.NewGuid(), Title = $"Season {number}", SeasonNumber = number, TvShowId = show.Id, LibraryId = library.Id };
            db.Add(season);
            var episodes = Enumerable.Range(1, 3)
                .Select(e => new Episode { Id = Guid.NewGuid(), Title = $"S{number}E{e}", EpisodeNumber = e, SeasonId = season.Id, LibraryId = library.Id })
                .ToList();
            db.AddRange(episodes);
            return (season, episodes);
        }

        var six = AddSeason(6);
        var seven = AddSeason(7);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Shield(show, six.Season, seven.Season, six.Episodes, seven.Episodes);
    }

    private async Task WatchAsync(VoraDbContext db, IEnumerable<Episode> episodes)
    {
        db.UserMediaStates.AddRange(episodes.Select(e => new UserMediaState { ProfileId = _profile, MediaItemId = e.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_watched_season_is_marked_watched_and_one_with_episodes_left_counts_them()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes.Concat(shield.SevenEpisodes.Take(1)));
        var watched = new LibraryItemVM { Id = shield.SeasonSix.Id, Title = "Season 6", Type = "Season" };
        var started = new LibraryItemVM { Id = shield.SeasonSeven.Id, Title = "Season 7", Type = "Season" };

        await new UserMediaStateRepository(db).AttachLibraryItemUserStatesAsync(new[] { watched, started }, _profile);

        watched.IsPlayed.Should().BeTrue();
        watched.UnplayedItemCount.Should().Be(0);
        started.IsPlayed.Should().BeFalse();
        started.UnplayedItemCount.Should().Be(2);
    }

    [Fact]
    public async Task A_show_counts_the_episodes_left_across_its_seasons()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes);
        var show = new LibraryItemVM { Id = shield.Show.Id, Title = shield.Show.Title, Type = "TvShow" };

        await new UserMediaStateRepository(db).AttachLibraryItemUserStatesAsync(new[] { show }, _profile);

        show.IsPlayed.Should().BeFalse();
        show.UnplayedItemCount.Should().Be(3);
    }

    [Fact]
    public async Task An_episode_in_the_trash_is_not_left_to_watch()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes.Take(2).Concat(shield.SevenEpisodes));
        shield.SixEpisodes[2].MissingSince = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var show = new LibraryItemVM { Id = shield.Show.Id, Title = shield.Show.Title, Type = "TvShow" };
        var season = new LibraryItemVM { Id = shield.SeasonSix.Id, Title = "Season 6", Type = "Season" };

        await new UserMediaStateRepository(db).AttachLibraryItemUserStatesAsync(new[] { show, season }, _profile);

        show.IsPlayed.Should().BeTrue();
        season.IsPlayed.Should().BeTrue();
        season.UnplayedItemCount.Should().Be(0);
    }
}
