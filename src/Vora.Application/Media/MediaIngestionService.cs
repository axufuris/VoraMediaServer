using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Requests;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public class MediaIngestionService : IMediaIngestionService
{
    private const string MusicArtworkUrlPrefix = "/api/artwork/custom/";

    private readonly IMediaRepository _repository;
    private readonly ILibraryRepository _libraryRepository;
    private readonly IRequestManager _requestManager;
    private readonly IMusicRepository _musicRepository;
    private readonly ITaskQueueManager _taskQueue;
    private readonly ILogger<MediaIngestionService> _logger;
    private readonly string _artworkBasePath;

    public MediaIngestionService(
        IMediaRepository repository,
        ILibraryRepository libraryRepository,
        IRequestManager requestManager,
        IMusicRepository musicRepository,
        ITaskQueueManager taskQueue,
        IConfiguration config,
        ILogger<MediaIngestionService> logger)
    {
        _repository = repository;
        _libraryRepository = libraryRepository;
        _requestManager = requestManager;
        _musicRepository = musicRepository;
        _taskQueue = taskQueue;
        _logger = logger;

        var configured = config["StoragePaths:CustomArtwork"];
        _artworkBasePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        if (!Directory.Exists(_artworkBasePath))
        {
            Directory.CreateDirectory(_artworkBasePath);
        }
    }

    public async Task<(List<string> FolderPaths, string? ScannerRegex)> GetLibraryDetailsAsync(Guid libraryId)
    {
        var library = await _libraryRepository.GetProjectedByIdAsync(libraryId, l => new { l.FolderPaths, l.ScannerRegex });
        if (library == null) throw new InvalidOperationException($"Library {libraryId} not found.");

        return (library.FolderPaths, library.ScannerRegex);
    }

    public async Task<Guid?> GetLibraryIdForMediaAsync(Guid mediaItemId)
    {
        var item = await _repository.GetProjectedAsync(mediaItemId, m => new { m.LibraryId });
        return item?.LibraryId;
    }

    public Task<HashSet<string>> GetExistingLibraryPathsAsync(Guid libraryId) =>
        _repository.GetExistingLibraryPathsAsync(libraryId);

    public Task<List<string>> GetMediaFilePathsAsync(Guid mediaItemId) =>
        _repository.GetMediaFilePathsAsync(mediaItemId);

    public async Task<Guid> EnsureMovieAsync(Guid libraryId, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null, string? edition = null)
    {
        var movieId = await _repository.GetMovieIdByTitleAndYearAsync(title, year, libraryId);
        if (movieId.HasValue) return movieId.Value;

        var movie = new Movie
        {
            Title = title,
            LibraryId = libraryId,
            AddedAt = DateTime.UtcNow,
            ReleaseDate = year.HasValue ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            TmdbId = tmdbId,
            ImdbId = imdbId,
            TvdbId = tvdbId,
            Edition = edition
        };
        await _repository.AddMediaItemAsync(movie);

        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            await _requestManager.ResolveRequestAsync(tmdbId, "Movie", movie.Id);
        }

        return movie.Id;
    }

    public async Task<Guid> EnsureTvShowAsync(Guid libraryId, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null)
    {
        var showId = await _repository.GetTvShowIdByTitleAsync(title, libraryId);
        if (showId.HasValue) return showId.Value;

        var show = new TvShow
        {
            Title = title,
            SortTitle = title,
            OriginalTitle = title,
            LibraryId = libraryId,
            AddedAt = DateTime.UtcNow,
            ReleaseDate = year.HasValue ? new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null,
            TmdbId = tmdbId,
            ImdbId = imdbId,
            TvdbId = tvdbId
        };
        await _repository.AddMediaItemAsync(show);

        if (!string.IsNullOrWhiteSpace(tmdbId))
        {
            await _requestManager.ResolveRequestAsync(tmdbId, "TvShow", show.Id);
        }

        return show.Id;
    }

    public async Task<Guid> EnsureSeasonAsync(Guid libraryId, Guid tvShowId, int seasonNumber)
    {
        var seasonId = await _repository.GetSeasonIdByNumberAsync(tvShowId, seasonNumber);
        if (seasonId.HasValue) return seasonId.Value;

        var season = new Season
        {
            Title = $"Season {seasonNumber}",
            SortTitle = $"Season {seasonNumber}",
            OriginalTitle = $"Season {seasonNumber}",
            SeasonNumber = seasonNumber,
            TvShowId = tvShowId,
            LibraryId = libraryId
        };
        await _repository.AddMediaItemAsync(season);
        return season.Id;
    }

    public async Task<Guid> EnsureEpisodeAsync(Guid libraryId, Guid seasonId, int episodeNumber, string title, DateTime? airDate, string? edition = null)
    {
        var episodeId = await _repository.GetEpisodeIdByNumberAsync(seasonId, episodeNumber);
        if (episodeId.HasValue) return episodeId.Value;

        var episode = new Episode
        {
            Title = title,
            SortTitle = title,
            OriginalTitle = title,
            LibraryId = libraryId,
            SeasonId = seasonId,
            EpisodeNumber = episodeNumber,
            AddedAt = DateTime.UtcNow,
            ReleaseDate = airDate,
            Edition = edition
        };
        await _repository.AddMediaItemAsync(episode);
        return episode.Id;
    }

    public async Task<Guid> EnsureArtistAsync(Guid libraryId, string name, string? sortName, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? bannerBytes = null, string? bannerMimeType = null, byte[]? clearLogoBytes = null, string? clearLogoMimeType = null)
    {
        var existing = await _musicRepository.GetArtistByNameAsync(libraryId, name);
        if (existing != null)
        {
            bool changed = false;
            if (string.IsNullOrEmpty(existing.ArtworkUrl) && artworkBytes != null && !existing.IsLocked(nameof(existing.ArtworkUrl)))
            {
                existing.ArtworkUrl = SaveArtworkBytes("artist", $"{libraryId}_{name}", artworkBytes, artworkMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.BackgroundUrl) && backgroundBytes != null && !existing.IsLocked(nameof(existing.BackgroundUrl)))
            {
                existing.BackgroundUrl = SaveArtworkBytes("artist_bg", $"{libraryId}_{name}", backgroundBytes, backgroundMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.BannerUrl) && bannerBytes != null && !existing.IsLocked(nameof(existing.BannerUrl)))
            {
                existing.BannerUrl = SaveArtworkBytes("artist_banner", $"{libraryId}_{name}", bannerBytes, bannerMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.ClearLogoUrl) && clearLogoBytes != null && !existing.IsLocked(nameof(existing.ClearLogoUrl)))
            {
                existing.ClearLogoUrl = SaveArtworkBytes("artist_logo", $"{libraryId}_{name}", clearLogoBytes, clearLogoMimeType);
                changed = true;
            }
            if (changed)
            {
                await _musicRepository.UpdateArtistAsync(existing);
            }
            return existing.Id;
        }

        var artist = new Artist
        {
            Name = name,
            SortName = sortName,
            LibraryId = libraryId,
            ArtworkUrl = artworkBytes != null ? SaveArtworkBytes("artist", $"{libraryId}_{name}", artworkBytes, artworkMimeType) : null,
            BackgroundUrl = backgroundBytes != null ? SaveArtworkBytes("artist_bg", $"{libraryId}_{name}", backgroundBytes, backgroundMimeType) : null,
            BannerUrl = bannerBytes != null ? SaveArtworkBytes("artist_banner", $"{libraryId}_{name}", bannerBytes, bannerMimeType) : null,
            ClearLogoUrl = clearLogoBytes != null ? SaveArtworkBytes("artist_logo", $"{libraryId}_{name}", clearLogoBytes, clearLogoMimeType) : null
        };
        await _musicRepository.AddArtistAsync(artist);

        if (string.IsNullOrEmpty(artist.ArtworkUrl))
        {
            _taskQueue.QueueRefreshArtistArtwork(artist.Id, artist.Name);
        }

        return artist.Id;
    }

    public async Task<Guid> EnsureAlbumAsync(Guid libraryId, Guid artistId, string title, int? year, string? genre, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? discArtBytes = null, string? discArtMimeType = null, string? albumArtist = null, bool isCompilation = false)
    {
        var existing = await _musicRepository.GetAlbumByTitleAsync(artistId, title);
        if (existing != null)
        {
            bool changed = false;
            if (string.IsNullOrEmpty(existing.ArtworkUrl) && artworkBytes != null && !existing.IsLocked(nameof(existing.ArtworkUrl)))
            {
                existing.ArtworkUrl = SaveArtworkBytes("album", $"{artistId}_{title}", artworkBytes, artworkMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.BackgroundUrl) && backgroundBytes != null && !existing.IsLocked(nameof(existing.BackgroundUrl)))
            {
                existing.BackgroundUrl = SaveArtworkBytes("album_bg", $"{artistId}_{title}", backgroundBytes, backgroundMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.DiscArtUrl) && discArtBytes != null && !existing.IsLocked(nameof(existing.DiscArtUrl)))
            {
                existing.DiscArtUrl = SaveArtworkBytes("album_disc", $"{artistId}_{title}", discArtBytes, discArtMimeType);
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.AlbumArtist) && !string.IsNullOrWhiteSpace(albumArtist) && !existing.IsLocked(nameof(existing.AlbumArtist)))
            {
                existing.AlbumArtist = albumArtist;
                changed = true;
            }
            if (!existing.IsCompilation && isCompilation && !existing.IsLocked(nameof(existing.IsCompilation)))
            {
                existing.IsCompilation = true;
                changed = true;
            }
            if (changed)
            {
                await _musicRepository.UpdateAlbumAsync(existing);
            }
            return existing.Id;
        }

        var album = new Album
        {
            Title = title,
            SortTitle = title,
            ArtistId = artistId,
            LibraryId = libraryId,
            Year = year,
            Genre = genre,
            AlbumArtist = albumArtist,
            IsCompilation = isCompilation,
            ArtworkUrl = artworkBytes != null ? SaveArtworkBytes("album", $"{artistId}_{title}", artworkBytes, artworkMimeType) : null,
            BackgroundUrl = backgroundBytes != null ? SaveArtworkBytes("album_bg", $"{artistId}_{title}", backgroundBytes, backgroundMimeType) : null,
            DiscArtUrl = discArtBytes != null ? SaveArtworkBytes("album_disc", $"{artistId}_{title}", discArtBytes, discArtMimeType) : null
        };
        await _musicRepository.AddAlbumAsync(album);

        if (string.IsNullOrEmpty(album.ArtworkUrl))
        {
            _taskQueue.QueueRefreshAlbumArtwork(album.Id, album.Title);
        }

        return album.Id;
    }

    public async Task<Guid> EnsureTrackAsync(Guid libraryId, Guid albumId, string title, int trackNumber, int? discNumber, int? durationSeconds, string? audioCodec, int? sampleRate, int? bitrate, string? contentRating, string? trackArtist = null)
    {
        var existing = await _musicRepository.GetTrackByAlbumAndNumberAsync(albumId, trackNumber, discNumber);
        if (existing != null)
        {
            bool changed = false;
            if (!existing.IsLocked(nameof(existing.ContentRating))
                && existing.ContentRating == null
                && !string.IsNullOrWhiteSpace(contentRating))
            {
                existing.ContentRating = contentRating;
                changed = true;
            }
            if (string.IsNullOrEmpty(existing.Artist) && !string.IsNullOrWhiteSpace(trackArtist) && !existing.IsLocked(nameof(existing.Artist)))
            {
                existing.Artist = trackArtist;
                changed = true;
            }
            if (changed)
            {
                await _musicRepository.UpdateTrackAsync(existing);
            }
            return existing.Id;
        }

        var track = new Track
        {
            Title = title,
            SortTitle = title,
            OriginalTitle = title,
            LibraryId = libraryId,
            AlbumId = albumId,
            Artist = trackArtist,
            TrackNumber = trackNumber,
            DiscNumber = discNumber,
            DurationSeconds = durationSeconds,
            AudioCodec = audioCodec,
            SampleRate = sampleRate,
            Bitrate = bitrate,
            ContentRating = contentRating
        };
        await _repository.AddMediaItemAsync(track);
        return track.Id;
    }

    public async Task AddMediaPartAsync(Guid mediaItemId, string filePath, string? resolution)
    {
        var fileInfo = new FileInfo(filePath);
        var part = new MediaPart
        {
            FilePath = filePath,
            Resolution = resolution,
            FileSizeBytes = fileInfo.Length,
            MediaItemId = mediaItemId,
            Container = Path.GetExtension(filePath).TrimStart('.').ToLower()
        };
        await _repository.AddMediaPartAsync(part);
    }

    private string? SaveArtworkBytes(string kind, string identityKey, byte[] bytes, string? mimeType)
    {
        try
        {
            var ext = ExtensionForMime(mimeType);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(identityKey))).ToLowerInvariant()[..16];
            var fileName = $"music_{kind}_{hash}{ext}";
            var fullPath = Path.Combine(_artworkBasePath, fileName);

            if (!File.Exists(fullPath))
            {
                File.WriteAllBytes(fullPath, bytes);
            }
            return $"{MusicArtworkUrlPrefix}{fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save embedded artwork for {Key}", identityKey);
            return null;
        }
    }

    private static string ExtensionForMime(string? mime) => mime switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg"
    };
}
