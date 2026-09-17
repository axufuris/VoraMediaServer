using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Analysis;
using Vora.Application.Libraries;
using Vora.Application.Requests;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Plugins.Dtos;
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
    private readonly IMediaAnalyzerService _analyzerService;
    private readonly Vora.Application.Metadata.ReferenceWriteGate _writeGate;
    private readonly ISystemSettingsRepository _settingsRepo;
    private readonly ILogger<MediaIngestionService> _logger;
    private readonly string _artworkBasePath;

    public MediaIngestionService(
        IMediaRepository repository,
        ILibraryRepository libraryRepository,
        IRequestManager requestManager,
        IMusicRepository musicRepository,
        ITaskQueueManager taskQueue,
        IMediaAnalyzerService analyzerService,
        Vora.Application.Metadata.ReferenceWriteGate writeGate,
        ISystemSettingsRepository settingsRepo,
        IOptions<StoragePathsOptions> storagePaths,
        ILogger<MediaIngestionService> logger)
    {
        _repository = repository;
        _libraryRepository = libraryRepository;
        _requestManager = requestManager;
        _musicRepository = musicRepository;
        _taskQueue = taskQueue;
        _analyzerService = analyzerService;
        _writeGate = writeGate;
        _settingsRepo = settingsRepo;
        _logger = logger;

        var configured = storagePaths.Value.CustomArtwork;
        _artworkBasePath = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Storage", "CustomArtwork");

        if (!Directory.Exists(_artworkBasePath))
        {
            Directory.CreateDirectory(_artworkBasePath);
        }
    }

    public async Task<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)> GetLibraryDetailsAsync(LibraryHandle library)
    {
        var libraryId = library.Value;
        var row = await _libraryRepository.GetProjectedByIdAsync(libraryId, l => new { l.FolderPaths, l.ScannerRegex, l.ExcludeFilters });
        if (row == null) throw new InvalidOperationException($"Library {libraryId} not found.");

        var excludeFilters = row.ExcludeFilters ?? new List<string>();
        var settings = await _settingsRepo.GetSettingsAsync();
        var ignoredFolders = settings.ScanIgnoredFolders;
        if (ignoredFolders.Count > 0)
        {
            excludeFilters = excludeFilters
                .Concat(ignoredFolders)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (row.FolderPaths, row.ScannerRegex, excludeFilters);
    }

    public async Task<LibraryHandle?> GetLibraryForMediaAsync(MediaItemHandle item)
    {
        var row = await _repository.GetProjectedAsync(item.Value, m => new { m.LibraryId });
        return row == null ? null : new LibraryHandle(row.LibraryId);
    }

    public Task<HashSet<string>> GetExistingLibraryPathsAsync(LibraryHandle library) =>
        _repository.GetExistingLibraryPathsAsync(library.Value);

    public Task<List<string>> GetLibraryItemFilePathsAsync(LibraryHandle library) =>
        _repository.GetLibraryItemFilePathsAsync(library.Value);

    public Task RemoveMediaItemByPathAsync(string filePath) =>
        _repository.DeleteMediaByFilePathAsync(filePath);

    public Task<List<string>> GetMediaFilePathsAsync(MediaItemHandle item) =>
        _repository.GetMediaFilePathsAsync(item.Value);

    public async Task<MediaItemHandle> EnsureMovieAsync(LibraryHandle library, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null, string? edition = null)
    {
        var libraryId = library.Value;
        var movieId = await _repository.GetMovieIdByExternalIdAsync(tmdbId, imdbId, libraryId)
            ?? await _repository.GetMovieIdByTitleAndYearAsync(title, year, libraryId);
        if (movieId.HasValue) return new MediaItemHandle(movieId.Value);

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

        return new MediaItemHandle(movie.Id);
    }

    public async Task<MediaItemHandle> EnsureTvShowAsync(LibraryHandle library, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null)
    {
        var libraryId = library.Value;

        var resultId = Guid.Empty;
        var createdId = (Guid?)null;

        await _writeGate.RunAsync(async () =>
        {
            var showId = await _repository.GetTvShowIdByExternalIdAsync(tmdbId, imdbId, libraryId)
                ?? await _repository.GetTvShowIdByTitleAndYearAsync(title, year, libraryId);
            if (showId.HasValue)
            {
                resultId = showId.Value;
                return;
            }

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
            resultId = show.Id;
            createdId = show.Id;
        });

        if (createdId.HasValue && !string.IsNullOrWhiteSpace(tmdbId))
        {
            await _requestManager.ResolveRequestAsync(tmdbId, "TvShow", createdId.Value);
        }

        return new MediaItemHandle(resultId);
    }

    public async Task<bool> SeasonExistsAsync(MediaItemHandle tvShow, int seasonNumber)
    {
        var seasonId = await _repository.GetSeasonIdByNumberAsync(tvShow.Value, seasonNumber);
        return seasonId.HasValue;
    }

    public async Task<SeasonHandle> EnsureSeasonAsync(LibraryHandle library, MediaItemHandle tvShow, int seasonNumber)
    {
        var libraryId = library.Value;
        var tvShowId = tvShow.Value;

        var resultId = Guid.Empty;

        await _writeGate.RunAsync(async () =>
        {
            var seasonId = await _repository.GetSeasonIdByNumberAsync(tvShowId, seasonNumber);
            if (seasonId.HasValue)
            {
                resultId = seasonId.Value;
                return;
            }

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
            resultId = season.Id;
        });

        return new SeasonHandle(resultId);
    }

    public async Task<MediaItemHandle> EnsureEpisodeAsync(LibraryHandle library, SeasonHandle season, int episodeNumber, string title, DateTime? airDate, string? edition = null, int? endEpisodeNumber = null)
    {
        var libraryId = library.Value;
        var seasonId = season.Value;
        var episodeId = await _repository.GetEpisodeIdByNumberAsync(seasonId, episodeNumber);
        if (episodeId.HasValue) return new MediaItemHandle(episodeId.Value);

        var episode = new Episode
        {
            Title = title,
            SortTitle = title,
            OriginalTitle = title,
            LibraryId = libraryId,
            SeasonId = seasonId,
            EpisodeNumber = episodeNumber,
            EndEpisodeNumber = endEpisodeNumber > episodeNumber ? endEpisodeNumber : null,
            AddedAt = DateTime.UtcNow,
            ReleaseDate = airDate,
            Edition = edition
        };
        await _repository.AddMediaItemAsync(episode);
        return new MediaItemHandle(episode.Id);
    }

    public async Task<ArtistHandle> EnsureArtistAsync(LibraryHandle library, string name, string? sortName, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? bannerBytes = null, string? bannerMimeType = null, byte[]? clearLogoBytes = null, string? clearLogoMimeType = null)
    {
        var libraryId = library.Value;
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
            return new ArtistHandle(existing.Id);
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

        return new ArtistHandle(artist.Id);
    }

    public async Task<AlbumHandle> EnsureAlbumAsync(LibraryHandle library, ArtistHandle artist, string title, int? year, string? genre, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? discArtBytes = null, string? discArtMimeType = null, string? albumArtist = null, bool isCompilation = false)
    {
        var libraryId = library.Value;
        var artistId = artist.Value;
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
            return new AlbumHandle(existing.Id);
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

        return new AlbumHandle(album.Id);
    }

    public async Task<MediaItemHandle> EnsureTrackAsync(LibraryHandle library, AlbumHandle album, string title, int trackNumber, int? discNumber, int? durationSeconds, string? audioCodec, int? sampleRate, int? bitrate, string? contentRating, string? trackArtist = null)
    {
        var libraryId = library.Value;
        var albumId = album.Value;
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
            return new MediaItemHandle(existing.Id);
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
        return new MediaItemHandle(track.Id);
    }

    public async Task AddMediaPartAsync(MediaItemHandle item, string filePath, string? resolution, string? edition = null)
    {
        var fileInfo = new FileInfo(filePath);
        var part = new MediaPart
        {
            FilePath = filePath,
            Resolution = resolution,
            Edition = edition,
            FileSizeBytes = fileInfo.Length,
            MediaItemId = item.Value,
            Container = Path.GetExtension(filePath).TrimStart('.').ToLower()
        };
        await _repository.AddMediaPartAsync(part);
        await _repository.SyncItemEditionFromPartsAsync(item.Value);
    }

    public async Task AttachLocalExtraAsync(LibraryHandle library, string parentTitle, int? parentYear, string filePath, string extraType, string title)
    {
        var movieId = await _repository.GetMovieIdByTitleAndYearAsync(parentTitle, parentYear, library.Value);
        if (movieId == null) return;
        await CreateAndAnalyzeExtraAsync(movieId.Value, filePath, extraType, title);
    }

    public async Task AttachTvShowLocalExtraAsync(LibraryHandle library, string showTitle, string filePath, string extraType, string title)
    {
        var showId = await _repository.GetTvShowIdByTitleAndYearAsync(showTitle, null, library.Value);
        if (showId == null) return;
        await CreateAndAnalyzeExtraAsync(showId.Value, filePath, extraType, title);
    }

    private async Task CreateAndAnalyzeExtraAsync(Guid parentId, string filePath, string extraType, string title)
    {
        var extra = new MediaExtra
        {
            MediaItemId = parentId,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(filePath) : title,
            ExtraType = Enum.TryParse<MediaExtraType>(extraType, ignoreCase: true, out var parsed) ? parsed : MediaExtraType.Other
        };
        await _repository.AddMediaExtraAsync(extra);

        var analysis = await _analyzerService.AnalyzeFileAsync(filePath);
        var fileInfo = new FileInfo(filePath);

        var part = new MediaPart
        {
            FilePath = filePath,
            MediaExtraId = extra.Id,
            Container = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant(),
            FileSizeBytes = analysis?.FileSizeBytes ?? (fileInfo.Exists ? fileInfo.Length : null),
            OverallBitrate = analysis?.OverallBitrate,
            Duration = analysis?.Duration
        };
        await _repository.AddMediaPartAsync(part);

        if (analysis == null) return;

        var incomingVideo = analysis.VideoTracks.Select(v => new MediaVideoTrack
        { StreamIndex = v.StreamIndex, Codec = v.Codec, Profile = v.Profile, HdrType = v.HdrType, BitDepth = v.BitDepth, Bitrate = v.Bitrate, IsDefault = v.IsDefault }).ToList();

        var incomingAudio = analysis.AudioTracks.Select(a => new MediaAudioTrack
        { StreamIndex = a.StreamIndex, Codec = a.Codec, Language = a.Language, Channels = a.Channels, Title = a.Title, IsDefault = a.IsDefault }).ToList();

        var incomingSubtitles = analysis.SubtitleTracks.Select(s => new MediaSubtitleTrack
        { StreamIndex = s.StreamIndex, Codec = s.Codec, Language = s.Language, Title = s.Title, IsDefault = s.IsDefault, IsForced = s.IsForced }).ToList();

        await _repository.SyncMediaTracksAsync(part.Id, incomingVideo, incomingAudio, incomingSubtitles);
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
