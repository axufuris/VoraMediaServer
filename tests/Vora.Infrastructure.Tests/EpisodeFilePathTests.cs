using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

public class EpisodeFilePathTests
{
    private readonly VoraDbContext _db = new(new DbContextOptionsBuilder<VoraDbContext>()
        .UseInMemoryDatabase("episode-file-path-tests-" + Guid.NewGuid().ToString("N"))
        .Options);

    private Season AddSeason(string showTitle)
    {
        var libraryId = Guid.NewGuid();
        var show = new TvShow { Id = Guid.NewGuid(), Title = showTitle, LibraryId = libraryId };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, TvShow = show, LibraryId = libraryId };
        _db.Set<TvShow>().Add(show);
        _db.Set<Season>().Add(season);
        return season;
    }

    private void AddEpisode(Season season, int number, string filePath)
    {
        var episode = new Episode { Id = Guid.NewGuid(), Title = $"Episode {number}", EpisodeNumber = number, SeasonId = season.Id, Season = season, LibraryId = season.LibraryId };
        episode.MediaParts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = filePath, MediaItemId = episode.Id });
        _db.Set<Episode>().Add(episode);
    }

    [Fact]
    public async Task A_show_gets_the_files_of_its_own_episodes()
    {
        var titus = AddSeason("Titus");
        AddEpisode(titus, 1, "/media/tv/Titus/Season 01/Titus.S01E01.mkv");
        AddEpisode(titus, 2, "/media/tv/Titus/Season 01/Titus.S01E02.mkv");
        AddEpisode(AddSeason("Mythic Quest"), 1, "/media/tv/Mythic Quest/Season 01/MQ.S01E01.mkv");
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var paths = await new MediaRepository(NullLogger<MediaRepository>.Instance, _db).GetEpisodeFilePathsForShowAsync(titus.TvShowId);

        paths.Should().BeEquivalentTo("/media/tv/Titus/Season 01/Titus.S01E01.mkv", "/media/tv/Titus/Season 01/Titus.S01E02.mkv");
    }
}
