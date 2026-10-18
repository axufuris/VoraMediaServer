using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class UpNextOrderTests
{
    private readonly Guid _profile = Guid.NewGuid();
    private readonly DateTime _evening = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("up-next-order-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Lioness(TvShow Show, Season SeasonOne, Season SeasonTwo, Movie Film, Dictionary<(int Season, int Episode), Episode> Episodes)
    {
        public Episode this[int season, int episode] => Episodes[(season, episode)];
    }

    private async Task<Lioness> SeedAsync(VoraDbContext db, UpNextOrder order)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Lioness", LibraryId = library.Id };
        var film = new Movie { Id = Guid.NewGuid(), Title = "Sicario", LibraryId = library.Id };
        db.AddRange(library, show, film, new UserProfile { Id = _profile, Name = "Viewer", UserId = Guid.NewGuid(), UpNextOrder = order });
        var episodes = new Dictionary<(int, int), Episode>();
        Season AddSeason(int number)
        {
            var season = new Season { Id = Guid.NewGuid(), Title = $"Season {number}", SeasonNumber = number, TvShowId = show.Id, LibraryId = library.Id };
            db.Add(season);
            for (var e = 1; e <= 8; e++)
            {
                var episode = new Episode { Id = Guid.NewGuid(), Title = $"S{number}E{e}", EpisodeNumber = e, SeasonId = season.Id, LibraryId = library.Id };
                db.Add(episode);
                episodes[(number, e)] = episode;
            }
            return season;
        }

        var lioness = new Lioness(show, AddSeason(1), AddSeason(2), film, episodes);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return lioness;
    }

    private async Task WatchAsync(VoraDbContext db, MediaItem item, bool played, double resume, int hoursAfterEvening)
    {
        db.UserMediaStates.Add(new UserMediaState
        {
            ProfileId = _profile, MediaItemId = item.Id, IsPlayed = played, ResumePositionSeconds = resume, LastPlayedAt = _evening.AddHours(hoursAfterEvening),
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid?> UpNextAsync(VoraDbContext db, Guid titleId) =>
        (await new UserMediaStateRepository(db).GetUpNextAsync(titleId, null, null, _profile)).NextItem?.Id;

    [Fact]
    public async Task Where_I_left_off_resumes_the_episode_played_last_rather_than_an_earlier_one()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        await WatchAsync(db, lioness[1, 2], played: false, resume: 600, hoursAfterEvening: 0);
        await WatchAsync(db, lioness[1, 7], played: false, resume: 900, hoursAfterEvening: 2);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[1, 7].Id);
        (await UpNextAsync(db, lioness.SeasonOne.Id)).Should().Be(lioness[1, 7].Id);
        (await new StreamRepository(db).ResolvePlayableMediaIdAsync(lioness.SeasonOne.Id, _profile)).Should().Be(lioness[1, 7].Id);
    }

    [Fact]
    public async Task In_order_resumes_the_earliest_episode_not_yet_watched()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.InOrder);
        await WatchAsync(db, lioness[1, 1], played: true, resume: 0, hoursAfterEvening: 0);
        await WatchAsync(db, lioness[1, 2], played: false, resume: 600, hoursAfterEvening: 1);
        await WatchAsync(db, lioness[1, 7], played: false, resume: 900, hoursAfterEvening: 2);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[1, 2].Id);
        (await new StreamRepository(db).ResolvePlayableMediaIdAsync(lioness.Show.Id, _profile)).Should().Be(lioness[1, 2].Id);
    }

    [Fact]
    public async Task Where_I_left_off_moves_on_to_the_episode_after_one_watched_to_the_end()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        await WatchAsync(db, lioness[1, 5], played: true, resume: 0, hoursAfterEvening: 1);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[1, 6].Id);
    }

    [Fact]
    public async Task Where_I_left_off_crosses_into_the_next_season_on_a_show_and_stays_in_the_season_on_a_season()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        await WatchAsync(db, lioness[1, 8], played: true, resume: 0, hoursAfterEvening: 1);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[2, 1].Id);
        (await UpNextAsync(db, lioness.SeasonOne.Id)).Should().Be(lioness[1, 1].Id);
    }

    [Fact]
    public async Task Where_I_left_off_starts_at_the_beginning_with_nothing_watched()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[1, 1].Id);
    }

    [Fact]
    public async Task Where_I_left_off_ignores_an_episode_only_looked_at()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        await WatchAsync(db, lioness[1, 3], played: false, resume: 900, hoursAfterEvening: 0);
        await WatchAsync(db, lioness[1, 6], played: false, resume: 0, hoursAfterEvening: 2);

        (await UpNextAsync(db, lioness.Show.Id)).Should().Be(lioness[1, 3].Id);
    }

    [Fact]
    public async Task Continue_watching_follows_the_profile_order()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        await WatchAsync(db, lioness[1, 2], played: false, resume: 600, hoursAfterEvening: 0);
        await WatchAsync(db, lioness[1, 7], played: false, resume: 900, hoursAfterEvening: 2);

        var rows = await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile);

        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(lioness[1, 7].Id);
    }

    [Fact]
    public async Task Continue_watching_leaves_out_a_show_whose_every_episode_was_watched_under_where_I_left_off()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.WhereILeftOff);
        var hour = 0;
        foreach (var episode in lioness.Episodes.Values) await WatchAsync(db, episode, played: true, resume: 0, hoursAfterEvening: hour++);

        (await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile)).Should().BeEmpty();
    }

    [Fact]
    public async Task Continue_watching_leaves_out_a_show_or_film_only_looked_at()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.InOrder);
        await WatchAsync(db, lioness[1, 1], played: false, resume: 12, hoursAfterEvening: 0);
        await WatchAsync(db, lioness.Film, played: false, resume: 12, hoursAfterEvening: 1);

        (await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile)).Should().BeEmpty();
    }

    [Fact]
    public async Task Continue_watching_keeps_a_film_watched_past_its_first_half_minute()
    {
        await using var db = NewContext();
        var lioness = await SeedAsync(db, UpNextOrder.InOrder);
        await WatchAsync(db, lioness.Film, played: false, resume: 45, hoursAfterEvening: 1);

        var rows = await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile);

        rows.Should().ContainSingle();
        rows[0].Id.Should().Be(lioness.Film.Id);
    }
}
