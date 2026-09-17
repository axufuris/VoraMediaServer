using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.SmartPlaylists;

public sealed class SmartPlaylistEvaluatorFixture : IDisposable
{
    public VoraDbContext Db { get; }
    public SmartPlaylistEvaluator Evaluator { get; }
    public Guid ProfileId { get; } = Guid.NewGuid();
    public Guid LibraryId { get; } = Guid.NewGuid();

    public SmartPlaylistEvaluatorFixture()
    {
        var options = new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("smartplaylist-tests-" + Guid.NewGuid().ToString("N"))
            .EnableSensitiveDataLogging()
            .Options;

        Db = new VoraDbContext(options);
        Evaluator = new SmartPlaylistEvaluator(Db);

        SeedLibrary();
    }

    private void SeedLibrary()
    {
        Db.Set<MediaLibrary>().Add(new MediaLibrary
        {
            Id = LibraryId,
            Name = "Test Library",
            Type = LibraryType.Movie,
            FolderPaths = new List<string> { "/media/movies" }
        });
        Db.SaveChanges();
    }

    public Movie AddMovie(string title, int? year, string? rating = null, IEnumerable<string>? genres = null, decimal? adminRating = null, decimal? audienceRating = null, DateTime? addedAt = null)
    {
        var movie = new Movie
        {
            Title = title,
            ReleaseDate = year.HasValue ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ContentRating = rating,
            LibraryId = LibraryId,
            AddedAt = addedAt ?? DateTime.UtcNow,
            ServerAdminRating = adminRating,
            ThirdPartyRating1 = audienceRating,
            Genres = (genres ?? Array.Empty<string>()).Select(g => new Genre { Name = g }).ToList()
        };
        Db.Set<Movie>().Add(movie);
        Db.SaveChanges();
        return movie;
    }

    public Artist AddArtist(string name)
    {
        var artist = new Artist { Name = name, LibraryId = LibraryId };
        Db.Set<Artist>().Add(artist);
        Db.SaveChanges();
        return artist;
    }

    public Album AddAlbum(Artist artist, string title, int? year = null, string? genre = null, bool isCompilation = false)
    {
        var album = new Album
        {
            Title = title,
            ArtistId = artist.Id,
            Year = year,
            Genre = genre,
            IsCompilation = isCompilation,
            LibraryId = LibraryId
        };
        Db.Set<Album>().Add(album);
        Db.SaveChanges();
        return album;
    }

    public Track AddTrack(Album album, string title, int trackNumber, string? contentRating = null, int? durationSeconds = null)
    {
        var track = new Track
        {
            Title = title,
            AlbumId = album.Id,
            TrackNumber = trackNumber,
            ContentRating = contentRating,
            DurationSeconds = durationSeconds,
            LibraryId = LibraryId
        };
        Db.Set<Track>().Add(track);
        Db.SaveChanges();
        return track;
    }

    public TvShow AddShow(string title, IEnumerable<string>? genres = null)
    {
        var show = new TvShow
        {
            Title = title,
            LibraryId = LibraryId,
            AddedAt = DateTime.UtcNow,
            Genres = (genres ?? Array.Empty<string>()).Select(g => new Genre { Name = g }).ToList()
        };
        Db.Set<TvShow>().Add(show);
        Db.SaveChanges();
        return show;
    }

    public Season AddSeason(TvShow show, int seasonNumber)
    {
        var season = new Season
        {
            Title = $"Season {seasonNumber}",
            TvShowId = show.Id,
            SeasonNumber = seasonNumber,
            LibraryId = LibraryId,
            AddedAt = DateTime.UtcNow
        };
        Db.Set<Season>().Add(season);
        Db.SaveChanges();
        return season;
    }

    public Episode AddEpisode(Season season, string title, int episodeNumber, int? releaseYear = null, string? contentRating = null, decimal? adminRating = null, decimal? audienceRating = null, DateTime? addedAt = null)
    {
        var ep = new Episode
        {
            Title = title,
            SeasonId = season.Id,
            EpisodeNumber = episodeNumber,
            ReleaseDate = releaseYear.HasValue ? new DateTime(releaseYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            ContentRating = contentRating,
            ServerAdminRating = adminRating,
            ThirdPartyRating1 = audienceRating,
            LibraryId = LibraryId,
            AddedAt = addedAt ?? DateTime.UtcNow
        };
        Db.Set<Episode>().Add(ep);
        Db.SaveChanges();
        return ep;
    }

    public void Dispose()
    {
        Db.Dispose();
    }
}
