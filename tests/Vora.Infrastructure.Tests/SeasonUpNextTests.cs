using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class SeasonUpNextTests
{
    private readonly Guid _profile = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("season-up-next-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Shogun(Season SeasonOne, Season SeasonTwo, Dictionary<(int Season, int Episode), Episode> Episodes)
    {
        public Episode this[int season, int episode] => Episodes[(season, episode)];
    }

    private static async Task<Shogun> SeedAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Shōgun", LibraryId = library.Id };
        db.AddRange(library, show);
        var episodes = new Dictionary<(int, int), Episode>();
        Season AddSeason(int number)
        {
            var season = new Season { Id = Guid.NewGuid(), Title = $"Season {number}", SeasonNumber = number, TvShowId = show.Id, LibraryId = library.Id };
            db.Add(season);
            for (var e = 1; e <= 3; e++)
            {
                var episode = new Episode { Id = Guid.NewGuid(), Title = $"S{number}E{e}", EpisodeNumber = e, SeasonId = season.Id, LibraryId = library.Id };
                db.Add(episode);
                episodes[(number, e)] = episode;
            }
            return season;
        }

        var shogun = new Shogun(AddSeason(1), AddSeason(2), episodes);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return shogun;
    }

    private async Task WatchAsync(VoraDbContext db, Episode episode, bool played, double resume = 0)
    {
        db.UserMediaStates.Add(new UserMediaState { ProfileId = _profile, MediaItemId = episode.Id, IsPlayed = played, ResumePositionSeconds = resume, LastPlayedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Up_next_on_a_season_is_its_episode_in_progress()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);
        await WatchAsync(db, shogun[1, 1], played: true);
        await WatchAsync(db, shogun[1, 2], played: false, resume: 1250);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(shogun.SeasonOne.Id, null, null, _profile);

        result.NextItem.Should().NotBeNull();
        result.NextItem?.Id.Should().Be(shogun[1, 2].Id);
        result.NextItem?.SeasonNumber.Should().Be(1);
    }

    [Fact]
    public async Task Up_next_on_a_season_stays_in_that_season()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(shogun.SeasonTwo.Id, null, null, _profile);

        result.NextItem?.Id.Should().Be(shogun[2, 1].Id);
    }

    [Fact]
    public async Task A_season_watched_to_the_end_starts_again_at_its_first_episode()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);
        foreach (var episode in new[] { shogun[2, 1], shogun[2, 2], shogun[2, 3] }) await WatchAsync(db, episode, played: true);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(shogun.SeasonTwo.Id, null, null, _profile);

        result.NextItem?.Id.Should().Be(shogun[2, 1].Id);
    }

    [Fact]
    public async Task An_episode_whose_file_is_gone_is_skipped()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);
        shogun[1, 1].MissingSince = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(shogun.SeasonOne.Id, null, null, _profile);

        result.NextItem?.Id.Should().Be(shogun[1, 2].Id);
    }

    [Fact]
    public async Task Playing_a_season_plays_its_episode_rather_than_asking_for_the_season_file()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);
        await WatchAsync(db, shogun[1, 1], played: true);
        await WatchAsync(db, shogun[1, 2], played: false, resume: 1250);

        var playable = await new StreamRepository(db).ResolvePlayableMediaIdAsync(shogun.SeasonOne.Id, _profile);
        var withoutProfile = await new StreamRepository(db).ResolvePlayableMediaIdAsync(shogun.SeasonTwo.Id, null);

        playable.Should().Be(shogun[1, 2].Id);
        withoutProfile.Should().Be(shogun[2, 1].Id);
    }

    [Fact]
    public async Task A_show_and_an_episode_resolve_as_they_did()
    {
        await using var db = NewContext();
        var shogun = await SeedAsync(db);
        await WatchAsync(db, shogun[1, 1], played: true);
        var repository = new StreamRepository(db);

        (await repository.ResolvePlayableMediaIdAsync(shogun.SeasonOne.TvShowId, _profile)).Should().Be(shogun[1, 2].Id);
        (await repository.ResolvePlayableMediaIdAsync(shogun[2, 3].Id, _profile)).Should().Be(shogun[2, 3].Id);
    }
}
