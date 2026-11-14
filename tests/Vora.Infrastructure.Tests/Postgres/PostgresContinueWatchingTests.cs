using Microsoft.EntityFrameworkCore;
using Vora.Application.Sync.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

// Continue Watching is one SQL statement (ContinueWatchingQuery), so these
// run on Postgres; the in-memory provider can't run raw SQL.
public class PostgresContinueWatchingTests(PostgresDatabase database) : IClassFixture<PostgresDatabase>
{
    private static readonly DateTime Evening = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

    private sealed class Show
    {
        public required TvShow TvShow { get; init; }
        public Dictionary<int, Season> Seasons { get; } = new();
        public Dictionary<(int Season, int Episode), Episode> Episodes { get; } = new();
        public Episode this[int season, int episode] => Episodes[(season, episode)];
    }

    // One viewer with a library of their own, so the tests can share a database.
    private sealed class Viewer
    {
        private readonly string _tag = Guid.NewGuid().ToString("N")[..8];
        private int _minutes;

        public Guid ProfileId { get; } = Guid.NewGuid();
        public MediaLibrary Library { get; }

        public Viewer()
        {
            Library = new MediaLibrary { Name = "Library " + _tag, Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv/" + _tag } };
        }

        public async Task AddProfileAsync(VoraDbContext db, UpNextOrder order)
        {
            var account = new User { Email = $"{_tag}@example.com", DisplayName = "Viewer " + _tag };
            db.AddRange(Library, account, new UserProfile { Id = ProfileId, Name = "Viewer", UserId = account.Id, UpNextOrder = order });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        public Show AddShow(VoraDbContext db, string title, params (int Season, int Episodes)[] seasons)
        {
            var show = new Show { TvShow = new TvShow { Title = $"{title} {_tag}", LibraryId = Library.Id } };
            db.Add(show.TvShow);
            foreach (var (number, count) in seasons)
            {
                var season = new Season { Title = $"Season {number}", SeasonNumber = number, TvShowId = show.TvShow.Id, LibraryId = Library.Id };
                db.Add(season);
                show.Seasons[number] = season;
                for (var e = 1; e <= count; e++)
                {
                    var episode = new Episode { Title = $"S{number}E{e}", EpisodeNumber = e, SeasonId = season.Id, LibraryId = Library.Id };
                    db.Add(episode);
                    show.Episodes[(number, e)] = episode;
                }
            }
            return show;
        }

        public Movie AddFilm(VoraDbContext db, string title)
        {
            var film = new Movie { Title = $"{title} {_tag}", LibraryId = Library.Id };
            db.Add(film);
            return film;
        }

        // Each watch is a minute after the last, unless it says when.
        public void Watch(VoraDbContext db, MediaItem item, bool played = false, double resume = 0, bool hidden = false, DateTime? at = null) =>
            db.UserMediaStates.Add(new UserMediaState
            {
                ProfileId = ProfileId,
                MediaItemId = item.Id,
                IsPlayed = played,
                ResumePositionSeconds = resume,
                IsHiddenFromContinueWatching = hidden,
                LastPlayedAt = at ?? Evening.AddMinutes(++_minutes),
            });

        public void Finish(VoraDbContext db, MediaItem item) => Watch(db, item, played: true);

        public Task<List<ContinueWatchingVM>> RowAsync(VoraDbContext db, int limit = 15) =>
            new UserMediaStateRepository(db).GetContinueWatchingAsync(ProfileId, limit);
    }

    private async Task<(Viewer Viewer, VoraDbContext Db)> ViewerAsync(UpNextOrder order)
    {
        var viewer = new Viewer();
        var db = database.NewContext();
        await viewer.AddProfileAsync(db, order);
        return (viewer, db);
    }

    private static Task SaveAsync(VoraDbContext db) => db.SaveChangesAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Where_I_left_off_offers_the_episode_played_last_rather_than_an_earlier_one()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 8), (2, 8));
        viewer.Watch(db, lioness[1, 2], resume: 600);
        viewer.Watch(db, lioness[1, 7], resume: 900);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(lioness[1, 7].Id);
    }

    [Fact]
    public async Task In_order_offers_the_first_episode_not_watched_to_the_end()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.InOrder);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 8), (2, 8));
        viewer.Finish(db, lioness[1, 1]);
        viewer.Watch(db, lioness[1, 2], resume: 600);
        viewer.Watch(db, lioness[1, 7], resume: 900);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(lioness[1, 2].Id);
    }

    [Fact]
    public async Task Where_I_left_off_moves_on_into_the_next_season_after_an_episode_watched_to_the_end()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 8), (2, 8));
        viewer.Finish(db, lioness[1, 8]);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(lioness[2, 1].Id);
    }

    [Fact]
    public async Task A_season_marked_watched_all_at_once_moves_on_from_its_last_episode()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 8), (2, 8));
        for (var e = 1; e <= 8; e++) viewer.Watch(db, lioness[1, e], played: true, at: Evening);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(lioness[2, 1].Id);
    }

    [Theory]
    [InlineData(UpNextOrder.WhereILeftOff)]
    [InlineData(UpNextOrder.InOrder)]
    public async Task A_show_whose_every_episode_was_watched_is_left_out(UpNextOrder order)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(order);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 4), (2, 4));
        foreach (var episode in lioness.Episodes.Values) viewer.Finish(db, episode);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_show_or_film_only_looked_at_is_left_out_and_a_film_past_its_first_half_minute_is_kept()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.InOrder);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 4));
        var glanced = viewer.AddFilm(db, "Heat");
        var started = viewer.AddFilm(db, "Sicario");
        viewer.Watch(db, lioness[1, 1], resume: 12);
        viewer.Watch(db, glanced, resume: 12);
        viewer.Watch(db, started, resume: 45);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(started.Id);
    }

    [Fact]
    public async Task Continue_watching_offers_the_next_episode_not_an_unwatched_special()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.InOrder);
        await using var _ = db;
        var titus = viewer.AddShow(db, "Titus", (0, 2), (1, 2));
        viewer.Finish(db, titus[1, 1]);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(titus[1, 2].Id);
    }

    [Fact]
    public async Task Continue_watching_keeps_a_special_that_is_part_watched()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.InOrder);
        await using var _ = db;
        var titus = viewer.AddShow(db, "Titus", (0, 2), (1, 2));
        viewer.Finish(db, titus[1, 1]);
        viewer.Watch(db, titus[0, 2], resume: 300);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(titus[0, 2].Id);
    }

    [Theory]
    [InlineData(UpNextOrder.WhereILeftOff)]
    [InlineData(UpNextOrder.InOrder)]
    public async Task A_show_with_only_specials_offers_its_next_special(UpNextOrder order)
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(order);
        await using var _ = db;
        var extras = viewer.AddShow(db, "Extras", (0, 3));
        viewer.Finish(db, extras[0, 1]);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(extras[0, 2].Id);
    }

    [Fact]
    public async Task An_episode_no_longer_on_disk_is_passed_over()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 4));
        lioness[1, 2].MissingSince = Evening;
        viewer.Finish(db, lioness[1, 1]);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(lioness[1, 3].Id);
    }

    [Fact]
    public async Task A_hidden_show_film_or_next_episode_is_left_out()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var hiddenShow = viewer.AddShow(db, "Lioness", (1, 4));
        var hiddenNext = viewer.AddShow(db, "Shōgun", (1, 4));
        var kept = viewer.AddShow(db, "Titus", (1, 4));
        var hiddenFilm = viewer.AddFilm(db, "Heat");
        viewer.Finish(db, hiddenShow[1, 1]);
        viewer.Watch(db, hiddenShow.TvShow, hidden: true);
        viewer.Finish(db, hiddenNext[1, 1]);
        viewer.Watch(db, hiddenNext[1, 2], hidden: true);
        viewer.Watch(db, hiddenFilm, resume: 600, hidden: true);
        viewer.Finish(db, kept[1, 1]);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().ContainSingle().Which.Id.Should().Be(kept[1, 2].Id);
    }

    [Fact]
    public async Task Films_and_shows_come_newest_first_up_to_the_limit()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var lioness = viewer.AddShow(db, "Lioness", (1, 4));
        var shogun = viewer.AddShow(db, "Shōgun", (1, 4));
        var heat = viewer.AddFilm(db, "Heat");
        var sicario = viewer.AddFilm(db, "Sicario");
        viewer.Finish(db, lioness[1, 1]);
        viewer.Watch(db, heat, resume: 600);
        viewer.Finish(db, shogun[1, 1]);
        viewer.Finish(db, lioness[1, 2]);
        viewer.Watch(db, sicario, resume: 600);
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Select(t => t.Id).Should().Equal(sicario.Id, lioness[1, 3].Id, shogun[1, 2].Id, heat.Id);
        (await viewer.RowAsync(db, limit: 2)).Select(t => t.Id).Should().Equal(sicario.Id, lioness[1, 3].Id);
    }

    [Fact]
    public async Task An_episode_tile_shows_its_show_and_names_the_season_it_opens()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        var shogun = viewer.AddShow(db, "Shōgun", (1, 3), (2, 3));
        shogun.TvShow.Overview = "Lord Toranaga fights for his life.";
        shogun.TvShow.PosterUrl = "/show-poster.jpg";
        shogun.TvShow.ContentRating = "TV-MA";
        shogun.TvShow.Genres.Add(new Genre { Name = "Drama " + Guid.NewGuid().ToString("N")[..6] });
        var episode = shogun[2, 2];
        episode.Overview = "The episode's own plot.";
        episode.PosterUrl = "/episode-still.jpg";
        episode.BackgroundUrl = "/episode-backdrop.jpg";
        episode.ReleaseDate = new DateOnly(2026, 3, 5);
        db.Add(new MediaItemAnalysis { MediaItemId = episode.Id, Duration = TimeSpan.FromMinutes(58) });
        viewer.Finish(db, shogun[2, 1]);
        viewer.Watch(db, episode, resume: 1250);
        await SaveAsync(db);

        var tile = (await viewer.RowAsync(db)).Should().ContainSingle().Subject;

        tile.Id.Should().Be(episode.Id);
        tile.Type.Should().Be("Episode");
        tile.Title.Should().Be("S2E2");
        tile.SortTitle.Should().BeNull();
        tile.Overview.Should().Be("Lord Toranaga fights for his life.");
        tile.PosterUrl.Should().Be("/show-poster.jpg");
        tile.BackgroundUrl.Should().Be("/episode-backdrop.jpg");
        tile.ReleaseDate.Should().Be(new DateOnly(2026, 3, 5));
        tile.ContentRating.Should().Be("TV-MA");
        tile.Genres.Should().Equal(shogun.TvShow.Genres.Select(g => g.Name));
        tile.TvShowId.Should().Be(shogun.TvShow.Id);
        tile.TvShowTitle.Should().Be(shogun.TvShow.Title);
        tile.SeasonId.Should().Be(shogun.Seasons[2].Id);
        tile.SeasonNumber.Should().Be(2);
        tile.EpisodeNumber.Should().Be(2);
        tile.ResumePositionSeconds.Should().Be(1250);
        tile.DurationSeconds.Should().Be(58 * 60);
    }

    [Fact]
    public async Task A_film_tile_is_the_film_itself()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.InOrder);
        await using var _ = db;
        var film = viewer.AddFilm(db, "The Heat");
        film.SortTitle = "Heat, The";
        film.Overview = "A cop and a thief.";
        film.PosterUrl = "/poster.jpg";
        film.BackgroundUrl = "/backdrop.jpg";
        film.ContentRating = "R";
        film.ReleaseDate = new DateOnly(1995, 12, 15);
        film.Genres.Add(new Genre { Name = "Crime " + Guid.NewGuid().ToString("N")[..6] });
        viewer.Watch(db, film, resume: 3600);
        await SaveAsync(db);

        var tile = (await viewer.RowAsync(db)).Should().ContainSingle().Subject;

        tile.Type.Should().Be("Movie");
        tile.Title.Should().Be(film.Title);
        tile.SortTitle.Should().Be("Heat, The");
        tile.Overview.Should().Be("A cop and a thief.");
        tile.PosterUrl.Should().Be("/poster.jpg");
        tile.BackgroundUrl.Should().Be("/backdrop.jpg");
        tile.ContentRating.Should().Be("R");
        tile.ReleaseDate.Should().Be(new DateOnly(1995, 12, 15));
        tile.Genres.Should().Equal(film.Genres.Select(g => g.Name));
        tile.ResumePositionSeconds.Should().Be(3600);
        tile.DurationSeconds.Should().BeNull();
        tile.TvShowId.Should().BeNull();
        tile.SeasonId.Should().BeNull();
        tile.SeasonNumber.Should().BeNull();
        tile.EpisodeNumber.Should().BeNull();
    }

    [Fact]
    public async Task A_profile_with_nothing_watched_has_an_empty_row()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        var (viewer, db) = await ViewerAsync(UpNextOrder.WhereILeftOff);
        await using var _ = db;
        viewer.AddShow(db, "Lioness", (1, 4));
        await SaveAsync(db);

        (await viewer.RowAsync(db)).Should().BeEmpty();
    }

    // The query against the per-show EpisodeToPlay version it replaced, over
    // random watch histories: specials, specials-only shows, episodes gone from
    // disk, hidden shows, films and episodes, finished and part-watched episodes.
    // Every watch has its own time, since ties were never ordered before.
    [Fact]
    public async Task Agrees_with_the_one_pick_per_show_version_on_random_histories()
    {
        Assert.SkipUnless(PostgresDatabase.IsConfigured, PostgresDatabase.SkipReason);
        for (var seed = 1; seed <= 40; seed++)
        {
            var random = new Random(seed);
            var (viewer, db) = await ViewerAsync(random.Next(2) == 0 ? UpNextOrder.InOrder : UpNextOrder.WhereILeftOff);
            await using var _ = db;
            var shows = new List<Show>
            {
                viewer.AddShow(db, "Lioness", (0, 2), (1, 5), (2, 4)),
                viewer.AddShow(db, "Shōgun", (1, 6)),
                viewer.AddShow(db, "Extras", (0, 4)),
                viewer.AddShow(db, "Titus", (0, 1), (1, 3), (2, 3), (3, 2)),
                viewer.AddShow(db, "Severance", (1, 4), (2, 4)),
            };
            var films = Enumerable.Range(1, 4).Select(i => viewer.AddFilm(db, "Film " + i)).ToList();
            foreach (var episode in shows.SelectMany(s => s.Episodes.Values).Where(_ => random.Next(8) == 0)) episode.MissingSince = Evening;
            if (random.Next(2) == 0) films[0].MissingSince = Evening;

            var items = shows.SelectMany(s => s.Episodes.Values).Cast<MediaItem>().Concat(films).OrderBy(_ => random.Next()).ToList();
            foreach (var item in items.Take(random.Next(items.Count)))
            {
                var roll = random.Next(10);
                viewer.Watch(db, item,
                    played: roll < 5,
                    resume: roll < 5 ? 0 : roll < 8 ? random.Next(30, 3000) : random.Next(0, 30),
                    hidden: random.Next(10) == 0);
            }
            foreach (var show in shows.Where(_ => random.Next(8) == 0)) viewer.Watch(db, show.TvShow, hidden: true);
            await SaveAsync(db);

            var limit = random.Next(2) == 0 ? 3 : 15;
            var expected = await new ContinueWatchingOracle(db).GetContinueWatchingAsync(viewer.ProfileId, limit);
            var actual = await viewer.RowAsync(db, limit);

            actual.Select(Describe).Should().Equal(expected.Select(Describe), $"seed {seed}");
        }
    }

    private static string Describe(ContinueWatchingVM t) =>
        $"{t.Id}|{t.Type}|{t.Title}|{t.SortTitle}|{t.Overview}|{t.PosterUrl}|{t.BackgroundUrl}|{t.ReleaseDate}|{t.ContentRating}|"
        + $"{string.Join(",", t.Genres.Order())}|{t.TvShowId}|{t.SeasonId}|{t.TvShowTitle}|{t.SeasonNumber}|{t.EpisodeNumber}|{t.ResumePositionSeconds}|{t.DurationSeconds}";
}
