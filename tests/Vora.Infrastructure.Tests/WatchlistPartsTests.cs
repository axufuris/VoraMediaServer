using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Analysis;
using Vora.Application.Requests;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Requests;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class WatchlistPartsTests
{
    private readonly Guid _profile = Guid.NewGuid();

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("watchlist-parts-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Seeded(TvShow Show, Season Season, Episode Episode, TvShow OtherShow, Season OtherSeason, Movie Movie);

    private static async Task<Seeded> SeedAsync(VoraDbContext db)
    {
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Library", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        var show = new TvShow { Id = Guid.NewGuid(), Title = "MobLand", TmdbId = "1396", PosterUrl = "/api/artwork/custom/mobland.jpg", LibraryId = library.Id };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = library.Id, PosterUrl = "/api/artwork/custom/season-one.jpg" };
        var episode = new Episode { Id = Guid.NewGuid(), Title = "Pilot", EpisodeNumber = 1, SeasonId = season.Id, LibraryId = library.Id };
        var otherShow = new TvShow { Id = Guid.NewGuid(), Title = "Severance", TmdbId = "95396", LibraryId = library.Id };
        var otherSeason = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = otherShow.Id, LibraryId = library.Id };
        var movie = new Movie { Id = Guid.NewGuid(), Title = "The Matrix", TmdbId = "603", LibraryId = library.Id };
        db.AddRange(library, show, season, episode, otherShow, otherSeason, movie);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seeded(show, season, episode, otherShow, otherSeason, movie);
    }

    private static WatchlistManager NewManager(VoraDbContext db)
    {
        var requests = Substitute.For<IRequestRepository>();
        requests.GetRequestsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>()).Returns(new Dictionary<string, MediaRequest>());
        return new WatchlistManager(
            new WatchlistRepository(db),
            new MediaRepository(NullLogger<MediaRepository>.Instance, db),
            requests,
            Substitute.For<IClientNotifier>(),
            NullLogger<WatchlistManager>.Instance);
    }

    private static WatchlistRequest Request(MediaItem item, string type) => new() { MediaItemId = item.Id, Type = type, Title = item.Title, PosterUrl = item.PosterUrl };

    private async Task<bool> OnAsync(WatchlistManager manager, MediaItem item) =>
        await manager.IsInWatchlistAsync(_profile, null, null, item.Id);

    [Fact]
    public async Task Watchlisting_a_season_puts_its_show_on_the_watchlist_and_it_reads_as_on_from_the_show_the_season_and_the_episode()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);

        (await OnAsync(manager, seeded.Show)).Should().BeFalse();
        (await OnAsync(manager, seeded.Season)).Should().BeFalse();
        (await OnAsync(manager, seeded.Episode)).Should().BeFalse();

        (await manager.ToggleAsync(_profile, Request(seeded.Season, "Season"))).Should().BeTrue();

        (await OnAsync(manager, seeded.Show)).Should().BeTrue();
        (await OnAsync(manager, seeded.Season)).Should().BeTrue();
        (await OnAsync(manager, seeded.Episode)).Should().BeTrue();
        (await OnAsync(manager, seeded.OtherShow)).Should().BeFalse();
        (await OnAsync(manager, seeded.OtherSeason)).Should().BeFalse();

        var list = await manager.GetWatchlistAsync(_profile);
        var entry = list.Should().ContainSingle().Subject;
        entry.Title.Should().Be("MobLand");
        entry.Type.Should().Be("TvShow");
        entry.MediaItemId.Should().Be(seeded.Show.Id);
        entry.ExternalId.Should().Be("1396");
        entry.PosterUrl.Should().Be("/api/artwork/custom/mobland.jpg");
    }

    [Fact]
    public async Task Taking_it_off_from_an_episode_takes_the_show_off_and_every_page_reads_as_off()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);
        await manager.ToggleAsync(_profile, Request(seeded.Season, "Season"));

        (await manager.ToggleAsync(_profile, Request(seeded.Episode, "Episode"))).Should().BeFalse();

        (await manager.GetWatchlistAsync(_profile)).Should().BeEmpty();
        (await OnAsync(manager, seeded.Show)).Should().BeFalse();
        (await OnAsync(manager, seeded.Season)).Should().BeFalse();
        (await OnAsync(manager, seeded.Episode)).Should().BeFalse();
    }

    [Fact]
    public async Task Watchlisting_the_show_reads_as_on_from_its_season_and_episode_and_a_season_takes_it_off()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);

        (await manager.ToggleAsync(_profile, Request(seeded.Show, "TvShow"))).Should().BeTrue();

        (await OnAsync(manager, seeded.Season)).Should().BeTrue();
        (await OnAsync(manager, seeded.Episode)).Should().BeTrue();

        (await manager.ToggleAsync(_profile, Request(seeded.Season, "Season"))).Should().BeFalse();
        (await manager.GetWatchlistAsync(_profile)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_season_an_episode_and_the_show_added_in_any_order_are_still_one_entry()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);

        await manager.ToggleAsync(_profile, Request(seeded.Episode, "Episode"));
        (await OnAsync(manager, seeded.Show)).Should().BeTrue();
        await manager.ToggleAsync(_profile, Request(seeded.Show, "TvShow"));
        (await OnAsync(manager, seeded.Season)).Should().BeFalse();
        await manager.ToggleAsync(_profile, Request(seeded.Season, "Season"));

        (await manager.GetWatchlistAsync(_profile)).Should().ContainSingle().Which.MediaItemId.Should().Be(seeded.Show.Id);
    }

    [Fact]
    public async Task A_movie_is_its_own_entry_and_does_not_touch_a_show()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);

        await manager.ToggleAsync(_profile, Request(seeded.Movie, "Movie"));

        (await OnAsync(manager, seeded.Movie)).Should().BeTrue();
        (await OnAsync(manager, seeded.Show)).Should().BeFalse();
        (await OnAsync(manager, seeded.Season)).Should().BeFalse();
        (await manager.GetWatchlistAsync(_profile)).Should().ContainSingle().Which.Type.Should().Be("Movie");
    }

    [Fact]
    public async Task Another_profile_does_not_see_the_show_as_watchlisted()
    {
        await using var db = NewContext();
        var seeded = await SeedAsync(db);
        var manager = NewManager(db);
        await manager.ToggleAsync(_profile, Request(seeded.Season, "Season"));

        (await manager.IsInWatchlistAsync(Guid.NewGuid(), null, null, seeded.Episode.Id)).Should().BeFalse();
    }
}
