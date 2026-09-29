using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests;

// Nothing in the generic metadata pipeline can enrich a track: the providers
// answer for movies, shows and seasons, so a track's LastMetadataRefresh is
// never stamped and its PosterUrl is always null — cover art lives on the Album.
// That made every track match "missing" on every scan forever, so a music
// library re-walked its whole contents each time, asking providers that had
// nothing to say.
public class EnrichmentTargetTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("enrichment-target-tests-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static Guid SeedMusicLibrary(VoraDbContext db, out Guid trackId)
    {
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Music",
            Type = LibraryType.Music,
            FolderPaths = new List<string> { "/media/music" }
        });

        var artist = new Artist { Id = Guid.NewGuid(), Name = "Nine Inch Nails", LibraryId = libraryId };
        var album = new Album { Id = Guid.NewGuid(), Title = "The Downward Spiral", ArtistId = artist.Id, LibraryId = libraryId };
        var track = new Track { Id = Guid.NewGuid(), Title = "Mr Self Destruct", AlbumId = album.Id, LibraryId = libraryId };

        db.Set<Artist>().Add(artist);
        db.Set<Album>().Add(album);
        db.Set<Track>().Add(track);
        db.SaveChanges();

        trackId = track.Id;
        return libraryId;
    }

    [Fact]
    public async Task A_track_is_never_a_metadata_target()
    {
        using var db = NewContext();
        var libraryId = SeedMusicLibrary(db, out var trackId);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var targets = await repo.GetMediaIdsMissingMetadataAsync(libraryId);

        targets.Should().NotContain(trackId);
    }

    [Fact]
    public async Task A_track_is_never_an_artwork_target()
    {
        using var db = NewContext();
        var libraryId = SeedMusicLibrary(db, out var trackId);
        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        var targets = await repo.GetMediaIdsMissingArtworkAsync(libraryId);

        targets.Should().NotContain(trackId);
    }

    // The exclusion must be about tracks, not about skipping work that is real.
    [Fact]
    public async Task A_movie_without_metadata_is_still_a_target()
    {
        using var db = NewContext();
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Movies",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        });
        var movie = new Movie { Id = Guid.NewGuid(), Title = "Inception", LibraryId = libraryId };
        db.Set<Movie>().Add(movie);
        db.SaveChanges();

        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        (await repo.GetMediaIdsMissingMetadataAsync(libraryId)).Should().Contain(movie.Id);
        (await repo.GetMediaIdsMissingArtworkAsync(libraryId)).Should().Contain(movie.Id);
    }

    private static (Guid LibraryId, TvShow Show, Season Season) SeedShow(VoraDbContext db)
    {
        var libraryId = Guid.NewGuid();
        db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = libraryId,
            Name = "Shows",
            Type = LibraryType.TvShow,
            FolderPaths = new List<string> { "/media/tv" }
        });
        var show = new TvShow { Id = Guid.NewGuid(), Title = "Titus", LibraryId = libraryId, PosterUrl = "show.jpg", LastMetadataRefresh = DateTime.UtcNow };
        var season = new Season { Id = Guid.NewGuid(), Title = "Season 1", SeasonNumber = 1, TvShowId = show.Id, LibraryId = libraryId, PosterUrl = "season.jpg", LastMetadataRefresh = DateTime.UtcNow };
        db.Set<TvShow>().Add(show);
        db.Set<Season>().Add(season);
        db.SaveChanges();
        return (libraryId, show, season);
    }

    [Fact]
    public async Task A_refreshed_episode_is_not_a_metadata_target_even_with_fields_the_provider_never_fills()
    {
        using var db = NewContext();
        var (libraryId, _, season) = SeedShow(db);
        db.Set<Episode>().Add(new Episode
        {
            Id = Guid.NewGuid(),
            Title = "Pilot",
            LibraryId = libraryId,
            SeasonId = season.Id,
            EpisodeNumber = 1,
            LastMetadataRefresh = DateTime.UtcNow.AddDays(-400)
        });
        db.SaveChanges();

        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        (await repo.GetMediaIdsMissingMetadataAsync(libraryId)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_episode_is_a_metadata_target()
    {
        using var db = NewContext();
        var (libraryId, _, season) = SeedShow(db);
        var episode = new Episode { Id = Guid.NewGuid(), Title = "Pilot", LibraryId = libraryId, SeasonId = season.Id, EpisodeNumber = 1 };
        db.Set<Episode>().Add(episode);
        db.SaveChanges();

        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        (await repo.GetMediaIdsMissingMetadataAsync(libraryId)).Should().BeEquivalentTo(new[] { episode.Id });
    }

    [Fact]
    public async Task A_show_with_a_season_missing_its_poster_is_a_metadata_target()
    {
        using var db = NewContext();
        var (libraryId, show, season) = SeedShow(db);
        season.PosterUrl = null;
        db.SaveChanges();

        var repo = new MediaRepository(NullLogger<MediaRepository>.Instance, db);

        (await repo.GetMediaIdsMissingMetadataAsync(libraryId)).Should().Contain(new[] { show.Id, season.Id });
    }
}
