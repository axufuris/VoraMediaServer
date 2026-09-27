using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class SpecialsUpNextTests
{
    private readonly Guid _profile = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("specials-up-next-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class Show
    {
        public required TvShow TvShow { get; init; }
        public Dictionary<(int Season, int Episode), Episode> Episodes { get; } = new();
        public Episode this[int season, int episode] => Episodes[(season, episode)];
    }

    private static async Task<Show> SeedAsync(VoraDbContext db, params (int Season, int Episodes)[] seasons)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var tvShow = new TvShow { Id = Guid.NewGuid(), Title = "Titus", LibraryId = library.Id };
        db.AddRange(library, tvShow);
        var show = new Show { TvShow = tvShow };

        foreach (var (seasonNumber, count) in seasons)
        {
            var season = new Season { Id = Guid.NewGuid(), Title = $"Season {seasonNumber}", SeasonNumber = seasonNumber, TvShowId = tvShow.Id, LibraryId = library.Id };
            db.Add(season);
            for (var e = 1; e <= count; e++)
            {
                var episode = new Episode { Id = Guid.NewGuid(), Title = $"S{seasonNumber}E{e}", EpisodeNumber = e, SeasonId = season.Id, LibraryId = library.Id };
                db.Add(episode);
                show.Episodes[(seasonNumber, e)] = episode;
            }
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return show;
    }

    private void Watch(VoraDbContext db, Episode episode, bool played = true, double resume = 0) =>
        db.UserMediaStates.Add(new UserMediaState
        {
            ProfileId = _profile,
            MediaItemId = episode.Id,
            IsPlayed = played,
            ResumePositionSeconds = resume,
            LastPlayedAt = DateTime.UtcNow,
        });

    [Fact]
    public async Task Play_next_on_a_show_starts_at_season_one_not_the_specials()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2), (1, 2));

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(show.TvShow.Id, null, null, _profile);

        result.NextItem!.Id.Should().Be(show[1, 1].Id);
    }

    [Fact]
    public async Task A_show_with_only_specials_starts_at_the_first_special()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2));

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(show.TvShow.Id, null, null, _profile);

        result.NextItem!.Id.Should().Be(show[0, 1].Id);
    }

    [Fact]
    public async Task A_special_plays_on_to_the_next_special_and_stops_after_the_last()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2), (1, 2));
        var repo = new UserMediaStateRepository(db);

        (await repo.GetUpNextAsync(show[0, 1].Id, null, null, _profile)).NextItem!.Id.Should().Be(show[0, 2].Id);
        (await repo.GetUpNextAsync(show[0, 2].Id, null, null, _profile)).NextItem.Should().BeNull();
    }

    [Fact]
    public async Task The_first_episode_has_no_previous_special()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2), (1, 2));

        var result = await new UserMediaStateRepository(db).GetUpNextAsync(show[1, 1].Id, null, null, _profile);

        result.PreviousItem.Should().BeNull();
        result.NextItem!.Id.Should().Be(show[1, 2].Id);
    }

    [Fact]
    public async Task Continue_watching_offers_the_next_episode_not_an_unwatched_special()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2), (1, 2));
        Watch(db, show[1, 1]);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rows = await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile);

        rows.Should().ContainSingle().Which.Id.Should().Be(show[1, 2].Id);
    }

    [Fact]
    public async Task Continue_watching_keeps_a_special_that_is_part_watched()
    {
        await using var db = NewContext();
        var show = await SeedAsync(db, (0, 2), (1, 2));
        Watch(db, show[1, 1]);
        Watch(db, show[0, 2], played: false, resume: 300);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rows = await new UserMediaStateRepository(db).GetContinueWatchingAsync(_profile);

        rows.Should().ContainSingle().Which.Id.Should().Be(show[0, 2].Id);
    }
}
