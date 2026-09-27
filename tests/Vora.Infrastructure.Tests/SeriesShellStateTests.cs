using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.SmartLists.Dtos;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class SeriesShellStateTests
{
    private static readonly DateTime August = new(2026, 8, 10, 23, 17, 9, DateTimeKind.Utc);
    private static readonly DateTime Today = new(2026, 9, 14, 20, 8, 21, DateTimeKind.Utc);

    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("series-shell-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static MediaRepository Repo(VoraDbContext db) => new(NullLogger<MediaRepository>.Instance, db);

    private sealed class Library
    {
        public required MediaLibrary Row { get; init; }
        public required VoraDbContext Db { get; init; }

        public TvShow Show(string title, DateTime? addedAt = null)
        {
            var show = new TvShow { Id = Guid.NewGuid(), Title = title, LibraryId = Row.Id, AddedAt = addedAt ?? August };
            Db.Add(show);
            return show;
        }

        public Season Season(TvShow show, int number)
        {
            var season = new Season { Id = Guid.NewGuid(), Title = $"Season {number}", SeasonNumber = number, TvShowId = show.Id, LibraryId = Row.Id, AddedAt = show.AddedAt };
            Db.Add(season);
            return season;
        }

        public Episode Episode(Season season, int number, string filePath, DateTime? missingSince = null, DateTime? addedAt = null)
        {
            var episode = new Episode { Id = Guid.NewGuid(), Title = $"Episode {number}", EpisodeNumber = number, SeasonId = season.Id, LibraryId = Row.Id, MissingSince = missingSince, AddedAt = addedAt ?? August };
            if (missingSince == null)
            {
                episode.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = filePath, MediaItemId = episode.Id });
            }
            Db.Add(episode);
            return episode;
        }
    }

    private static Library NewLibrary(VoraDbContext db)
    {
        var row = new MediaLibrary { Id = Guid.NewGuid(), Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/tv" } };
        db.Add(row);
        return new Library { Row = row, Db = db };
    }

    private static string MissingPath(string name) => Path.Combine(Path.GetTempPath(), "vora-missing-" + Guid.NewGuid().ToString("N"), name);

    private static async Task<(DateTime? Season, DateTime? Show)> MissingSinceAsync(VoraDbContext db, Season season, TvShow show)
    {
        db.ChangeTracker.Clear();
        var s = await db.Set<Season>().AsNoTracking().SingleAsync(x => x.Id == season.Id);
        var t = await db.Set<TvShow>().AsNoTracking().SingleAsync(x => x.Id == show.Id);
        return (s.MissingSince, t.MissingSince);
    }

    [Fact]
    public async Task Losing_the_last_episode_file_hides_the_season_and_show()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("The Walking Dead - Dead City [imdb-]");
        var season = lib.Season(show, 3);
        var path = MissingPath("S03E08.mkv");
        lib.Episode(season, 8, path);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).MarkMediaMissingByFilePathAsync(path);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, season, show);
        seasonMissing.Should().NotBeNull();
        showMissing.Should().NotBeNull();
    }

    [Fact]
    public async Task Losing_one_of_several_episodes_leaves_the_season_visible()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        var season = lib.Season(show, 3);
        var path = MissingPath("S03E02.mkv");
        lib.Episode(season, 2, path);
        lib.Episode(season, 8, MissingPath("S03E08.mkv"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).MarkMediaMissingByFilePathAsync(path);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, season, show);
        seasonMissing.Should().BeNull();
        showMissing.Should().BeNull();
    }

    [Fact]
    public async Task An_emptied_season_is_hidden_while_the_show_stays_for_its_other_seasons()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        var seasonOne = lib.Season(show, 1);
        var seasonThree = lib.Season(show, 3);
        lib.Episode(seasonOne, 1, MissingPath("S01E01.mkv"));
        var path = MissingPath("S03E08.mkv");
        lib.Episode(seasonThree, 8, path);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).MarkMediaMissingByFilePathAsync(path);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, seasonThree, show);
        seasonMissing.Should().NotBeNull();
        showMissing.Should().BeNull();
    }

    [Fact]
    public async Task Restoring_an_episode_from_trash_brings_its_season_and_show_back()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = Today;
        var season = lib.Season(show, 3);
        season.MissingSince = Today;
        var episode = lib.Episode(season, 8, MissingPath("S03E08.mkv"), missingSince: Today);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RestoreMissingMediaAsync(episode.Id);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, season, show);
        seasonMissing.Should().BeNull();
        showMissing.Should().BeNull();
    }

    [Fact]
    public async Task A_shell_cannot_be_restored_on_its_own()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = Today;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RestoreMissingMediaAsync(show.Id);

        db.ChangeTracker.Clear();
        (await db.Set<TvShow>().AsNoTracking().SingleAsync(t => t.Id == show.Id, TestContext.Current.CancellationToken)).MissingSince.Should().Be(Today);
    }

    [Fact]
    public async Task Deleting_the_last_episode_hides_the_season_and_show()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        var season = lib.Season(show, 3);
        var episode = lib.Episode(season, 8, MissingPath("S03E08.mkv"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).DeleteMediaItemAsync(episode.Id);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, season, show);
        seasonMissing.Should().NotBeNull();
        showMissing.Should().NotBeNull();
    }

    // The home-page complaint: a season created in August gains an episode today.
    [Fact]
    public async Task New_content_marks_the_season_and_show_as_recently_added()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("The Walking Dead: Dead City");
        var season = lib.Season(show, 3);
        lib.Episode(season, 8, MissingPath("S03E08.mkv"), addedAt: Today);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RefreshSeriesStateAsync(season.Id, Today);

        db.ChangeTracker.Clear();
        (await db.Set<Season>().AsNoTracking().SingleAsync(s => s.Id == season.Id, TestContext.Current.CancellationToken)).LastContentAddedAt.Should().Be(Today);
        (await db.Set<TvShow>().AsNoTracking().SingleAsync(t => t.Id == show.Id, TestContext.Current.CancellationToken)).LastContentAddedAt.Should().Be(Today);
    }

    [Fact]
    public async Task Recently_added_never_moves_backwards()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.LastContentAddedAt = Today;
        var season = lib.Season(show, 3);
        season.LastContentAddedAt = Today;
        lib.Episode(season, 2, MissingPath("S03E02.mkv"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RefreshSeriesStateAsync(season.Id, August);

        db.ChangeTracker.Clear();
        (await db.Set<TvShow>().AsNoTracking().SingleAsync(t => t.Id == show.Id, TestContext.Current.CancellationToken)).LastContentAddedAt.Should().Be(Today);
    }

    [Fact]
    public async Task New_content_in_a_hidden_season_brings_it_back()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = August;
        var season = lib.Season(show, 3);
        season.MissingSince = August;
        lib.Episode(season, 8, MissingPath("S03E08.mkv"), addedAt: Today);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RefreshSeriesStateAsync(season.Id, Today);

        var (seasonMissing, showMissing) = await MissingSinceAsync(db, season, show);
        seasonMissing.Should().BeNull();
        showMissing.Should().BeNull();
    }

    [Fact]
    public async Task After_a_merge_a_trashed_keeper_episode_that_now_has_a_file_is_revived()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("The Walking Dead: Dead City");
        var season = lib.Season(show, 3);
        season.MissingSince = Today;
        show.MissingSince = Today;
        var keeperEpisode = lib.Episode(season, 8, MissingPath("unused.mkv"), missingSince: Today, addedAt: Today);
        keeperEpisode.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = MissingPath("moved-in.mkv"), MediaItemId = keeperEpisode.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo(db).RefreshShowStateAsync(show.Id);

        db.ChangeTracker.Clear();
        (await db.Set<Episode>().AsNoTracking().SingleAsync(e => e.Id == keeperEpisode.Id, TestContext.Current.CancellationToken)).MissingSince.Should().BeNull();
        var refreshedSeason = await db.Set<Season>().AsNoTracking().SingleAsync(s => s.Id == season.Id, TestContext.Current.CancellationToken);
        refreshedSeason.MissingSince.Should().BeNull();
        refreshedSeason.LastContentAddedAt.Should().Be(Today);
    }

    [Fact]
    public async Task Trash_lists_the_episodes_but_not_the_shells_around_them()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = Today;
        var season = lib.Season(show, 3);
        season.MissingSince = Today;
        var episode = lib.Episode(season, 8, MissingPath("S03E08.mkv"), missingSince: Today);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var trash = await Repo(db).GetMissingMediaAsync();

        trash.Should().ContainSingle().Which.Id.Should().Be(episode.Id);
    }

    // Purging a show first would cascade-delete its episodes without archiving
    // their watch history. Shells wait until their episodes are gone.
    [Fact]
    public async Task Expired_trash_never_includes_a_shell_that_still_holds_episodes()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = August;
        var season = lib.Season(show, 3);
        season.MissingSince = August;
        var episode = lib.Episode(season, 8, MissingPath("S03E08.mkv"), missingSince: August);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = Repo(db);

        (await repo.GetExpiredMissingMediaIdsAsync(Today)).Should().Equal(episode.Id);
        (await repo.GetEmptyTrashedSeasonIdsAsync()).Should().BeEmpty();
        (await repo.GetEmptyTrashedShowIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Once_its_episodes_are_purged_an_empty_shell_is_purgeable()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var show = lib.Show("Dead City");
        show.MissingSince = August;
        var season = lib.Season(show, 3);
        season.MissingSince = August;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repo = Repo(db);

        (await repo.GetEmptyTrashedSeasonIdsAsync()).Should().Equal(season.Id);

        await repo.DeleteMediaItemAsync(season.Id);

        (await repo.GetEmptyTrashedShowIdsAsync()).Should().Equal(show.Id);
    }

    [Fact]
    public async Task A_live_empty_show_is_not_purged()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        lib.Show("Brand New Show");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repo(db).GetEmptyTrashedShowIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Recently_added_lists_a_show_by_its_newest_episode()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var olderShowWithNewEpisode = lib.Show("The Walking Dead: Dead City", addedAt: August);
        olderShowWithNewEpisode.LastContentAddedAt = Today;
        var newerShow = lib.Show("Lanterns", addedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            null, lib.Row.Id, new SmartListRulesDto { MediaTypes = ["TvShow"] }, SmartListSortBy.DateAddedDesc, 10);

        items.Select(i => i.Title).Should().Equal("The Walking Dead: Dead City", "Lanterns");
        items[0].LastContentAddedAt.Should().Be(Today);
    }

    [Fact]
    public async Task Recently_added_hides_an_empty_shell()
    {
        using var db = NewContext();
        var lib = NewLibrary(db);
        var shell = lib.Show("The Walking Dead - Dead City [imdb-]", addedAt: Today);
        shell.MissingSince = Today;
        lib.Show("Lanterns");
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var items = await new SmartListRepository(db).GetSmartListItemsAsync(
            null, lib.Row.Id, new SmartListRulesDto { MediaTypes = ["TvShow"] }, SmartListSortBy.DateAddedDesc, 10);

        items.Select(i => i.Title).Should().Equal("Lanterns");
    }
}
