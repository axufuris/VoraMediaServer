using Microsoft.EntityFrameworkCore;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Libraries.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

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

    private async Task WatchAsync(VoraDbContext db, IEnumerable<MediaItem> episodes)
    {
        db.UserMediaStates.AddRange(episodes.Select(e => new UserMediaState { ProfileId = _profile, MediaItemId = e.Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task<LibraryItemVM> ProjectAsync(VoraDbContext db, Guid id, Guid? profileId) =>
        db.MediaItems.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(LibraryItemVM.ProjectionFor(profileId, db.UserMediaStates, db.UserMediaRatings))
            .SingleAsync(TestContext.Current.CancellationToken);

    private Task<LibraryItemVM> ProjectAsync(VoraDbContext db, Guid id) => ProjectAsync(db, id, _profile);

    [Fact]
    public async Task A_watched_season_is_marked_watched_and_one_with_episodes_left_counts_them()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes.Concat(shield.SevenEpisodes.Take(1)));

        var watched = await ProjectAsync(db, shield.SeasonSix.Id);
        var started = await ProjectAsync(db, shield.SeasonSeven.Id);

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

        var show = await ProjectAsync(db, shield.Show.Id);

        show.IsPlayed.Should().BeFalse();
        show.UnplayedItemCount.Should().Be(3);
    }

    [Fact]
    public async Task Only_this_profile_s_finished_episodes_count_as_watched()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        db.UserMediaStates.AddRange(
            new UserMediaState { ProfileId = Guid.NewGuid(), MediaItemId = shield.SixEpisodes[0].Id, IsPlayed = true, LastPlayedAt = DateTime.UtcNow },
            new UserMediaState { ProfileId = _profile, MediaItemId = shield.SixEpisodes[1].Id, IsPlayed = false, ResumePositionSeconds = 600, LastPlayedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await WatchAsync(db, shield.SixEpisodes.Skip(2));

        var show = await ProjectAsync(db, shield.Show.Id);
        var season = await ProjectAsync(db, shield.SeasonSix.Id);

        show.UnplayedItemCount.Should().Be(5);
        season.UnplayedItemCount.Should().Be(2);
        season.IsPlayed.Should().BeFalse();
    }

    [Fact]
    public async Task An_episode_in_the_trash_is_not_left_to_watch()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes.Take(2).Concat(shield.SevenEpisodes));
        shield.SixEpisodes[2].MissingSince = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var show = await ProjectAsync(db, shield.Show.Id);
        var season = await ProjectAsync(db, shield.SeasonSix.Id);

        show.IsPlayed.Should().BeTrue();
        season.IsPlayed.Should().BeTrue();
        season.UnplayedItemCount.Should().Be(0);
    }

    [Fact]
    public async Task A_film_carries_this_profile_s_watched_state_and_rating()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        var film = new Movie { Id = Guid.NewGuid(), Title = "The Avengers", LibraryId = shield.Show.LibraryId };
        db.Add(film);
        db.UserMediaRatings.AddRange(
            new UserMediaRating { ProfileId = _profile, MediaItemId = film.Id, Rating = 8.5m },
            new UserMediaRating { ProfileId = Guid.NewGuid(), MediaItemId = film.Id, Rating = 2m });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await WatchAsync(db, new[] { film });

        var item = await ProjectAsync(db, film.Id);

        item.IsPlayed.Should().BeTrue();
        item.MyRating.Should().Be(8.5m);
        item.UnplayedItemCount.Should().BeNull();
    }

    [Fact]
    public async Task Without_a_profile_nothing_is_watched_or_counted()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        await WatchAsync(db, shield.SixEpisodes.Concat(shield.SevenEpisodes));

        var show = await ProjectAsync(db, shield.Show.Id, null);

        show.IsPlayed.Should().BeFalse();
        show.UnplayedItemCount.Should().BeNull();
        show.MyRating.Should().BeNull();
    }

    [Fact]
    public async Task A_collection_s_show_is_counted_the_same_way_trashed_episodes_left_out()
    {
        await using var db = NewContext();
        var shield = await SeedAsync(db);
        var collection = new Collection { Title = "Marvel Television" };
        collection.Items.Add(shield.Show);
        db.Add(collection);
        await WatchAsync(db, shield.SixEpisodes.Take(2).Concat(shield.SevenEpisodes));
        shield.SixEpisodes[2].MissingSince = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var details = await db.Collections.AsNoTracking()
            .Where(c => c.Id == collection.Id)
            .Select(CollectionDetailsVM.ProjectionFor(_profile, db.UserMediaStates))
            .SingleAsync(TestContext.Current.CancellationToken);

        var show = details.Items.Single();
        show.IsPlayed.Should().BeTrue();
        show.UnplayedItemCount.Should().Be(0);
    }
}
