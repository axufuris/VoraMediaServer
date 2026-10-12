using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.LibraryMigration;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class EpisodeRangeTests
{
    private const string DoubleEpisode = "/media/tv/Ghosts (US) (2021)/Season 05/Ghosts (US) (2021) - S05E09-E10 - Its a Wonderful Christmas Carol.mkv";
    private const string SingleEpisode = "/media/tv/Ghosts (US) (2021)/Season 05/Ghosts (US) (2021) - S05E11 - The Christmas Spirit.mkv";

    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("episode-range-tests-" + Guid.NewGuid().ToString("N"))
        .Options);

    private readonly Guid _libraryId = Guid.NewGuid();
    private readonly Guid _seasonId = Guid.NewGuid();

    public EpisodeRangeTests()
    {
        var show = new TvShow { Title = "Ghosts (US)", LibraryId = _libraryId };
        _db.AddRange(
            new MediaLibrary { Id = _libraryId, Name = "Shows", Type = LibraryType.TvShow, FolderPaths = new List<string> { "/media/tv" } },
            show,
            new Season { Id = _seasonId, Title = "Season 5", SeasonNumber = 5, TvShowId = show.Id, LibraryId = _libraryId });
    }

    private MediaRepository Repo() => new(NullLogger<MediaRepository>.Instance, _db);

    private Episode AddEpisode(int number, string filePath, int? endNumber = null)
    {
        var episode = new Episode { Title = $"Episode {number}", EpisodeNumber = number, EndEpisodeNumber = endNumber, SeasonId = _seasonId, LibraryId = _libraryId };
        episode.MediaParts.Add(new MediaPart { FilePath = filePath, MediaItemId = episode.Id });
        _db.Add(episode);
        return episode;
    }

    [Fact]
    public async Task A_known_multi_episode_file_gets_its_range_recorded_once()
    {
        var christmas = AddEpisode(9, DoubleEpisode);
        var spirit = AddEpisode(11, SingleEpisode);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var ranges = new Dictionary<string, int> { [DoubleEpisode] = 10 };

        (await Repo().SetEpisodeEndNumbersAsync(_libraryId, ranges)).Should().Be(1);
        (await Repo().SetEpisodeEndNumbersAsync(_libraryId, ranges)).Should().Be(0);

        christmas.EndEpisodeNumber.Should().Be(10);
        spirit.EndEpisodeNumber.Should().BeNull();
    }

    [Fact]
    public async Task Another_librarys_file_with_the_same_path_is_left_alone()
    {
        var christmas = AddEpisode(9, DoubleEpisode);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Repo().SetEpisodeEndNumbersAsync(Guid.NewGuid(), new Dictionary<string, int> { [DoubleEpisode] = 10 })).Should().Be(0);

        christmas.EndEpisodeNumber.Should().BeNull();
    }

    [Fact]
    public async Task A_new_file_can_widen_an_episodes_range_but_never_narrow_it()
    {
        var finale = AddEpisode(17, "/media/tv/Lost (2004)/Season 06/Lost (2004) - S06E17.mkv");
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Repo().WidenEpisodeRangeAsync(finale.Id, 18);
        await Repo().WidenEpisodeRangeAsync(finale.Id, 17);

        finale.EndEpisodeNumber.Should().Be(18);
    }

    [Fact]
    public async Task Changing_a_server_rating_marks_its_poster_overlay_out_of_date()
    {
        var overlaid = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var otherLibrary = Guid.NewGuid();
        var changed = new Movie { Title = "Heat", LibraryId = _libraryId, ServerAdminRating = 7m, LastOverlayGeneratedAt = overlaid };
        var unchanged = new Movie { Title = "Ronin", LibraryId = otherLibrary, ServerAdminRating = 8m, LastOverlayGeneratedAt = overlaid };
        _db.AddRange(changed, unchanged);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var libraries = await new LibraryMigrationRepository(_db).BulkSetAdminRatingsAsync(
        [
            new RatingUpsert { MediaItemId = changed.Id, Rating = 9m },
            new RatingUpsert { MediaItemId = unchanged.Id, Rating = 8m },
        ]);

        libraries.Should().Equal(_libraryId);
        changed.ServerAdminRating.Should().Be(9m);
        changed.LastOverlayGeneratedAt.Should().BeNull();
        unchanged.LastOverlayGeneratedAt.Should().Be(overlaid);
    }
}
