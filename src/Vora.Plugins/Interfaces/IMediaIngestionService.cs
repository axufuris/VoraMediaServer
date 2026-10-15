using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IMediaIngestionService
{
    Task<(List<string> FolderPaths, string? ScannerRegex, List<string> ExcludeFilters)> GetLibraryDetailsAsync(LibraryHandle library);
    Task<LocalMetadataMode> GetLocalMetadataModeAsync(LibraryHandle library);
    Task<MediaItemHandle?> FindHandMatchedOwnerAsync(LibraryHandle library, string knownFilePath);
    Task<LibraryHandle?> GetLibraryForMediaAsync(MediaItemHandle item);
    Task<HashSet<string>> GetExistingLibraryPathsAsync(LibraryHandle library);
    Task SetEpisodeRangesAsync(LibraryHandle library, IReadOnlyDictionary<string, EpisodeRange> rangeByFile);
    Task<List<string>> GetFirstEpisodeFilePathsAsync(LibraryHandle library);
    Task<List<MusicFilePlacement>> GetMusicFilePlacementsAsync(LibraryHandle library);
    Task ForgetAlbumArtAndYearAsync(Guid albumId);
    Task<List<MovieFilePlacement>> GetFilesOfMoviesWithSeveralFilesAsync(LibraryHandle library);
    Task ReportUnreadableEpisodeFilesAsync(LibraryHandle library, IReadOnlyList<string> filePaths);
    Task<List<string>> GetLibraryItemFilePathsAsync(LibraryHandle library);
    Task RemoveMediaItemByPathAsync(string filePath);
    Task ReleaseTrackedEntitiesAsync();
    Task<List<string>> GetMediaFilePathsAsync(MediaItemHandle item);

    Task<MediaItemHandle> EnsureMovieAsync(LibraryHandle library, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null, string? edition = null);
    Task<MediaItemHandle> EnsureTvShowAsync(LibraryHandle library, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null);

    Task<MediaItemHandle> EnsureMovieAsync(LibraryHandle library, string title, int? year, ExternalIdSet externalIds, string? edition = null)
        => EnsureMovieAsync(library, title, year, externalIds.TmdbId, externalIds.ImdbId, externalIds.TvdbId, edition);

    Task<MediaItemHandle> EnsureTvShowAsync(LibraryHandle library, string title, int? year, ExternalIdSet externalIds)
        => EnsureTvShowAsync(library, title, year, externalIds.TmdbId, externalIds.ImdbId, externalIds.TvdbId);

    Task<bool> SeasonExistsAsync(MediaItemHandle tvShow, int seasonNumber);
    Task<SeasonHandle> EnsureSeasonAsync(LibraryHandle library, MediaItemHandle tvShow, int seasonNumber);
    Task<MediaItemHandle> EnsureEpisodeAsync(LibraryHandle library, SeasonHandle season, int episodeNumber, string title, DateOnly? airDate, string? edition = null, int? endEpisodeNumber = null);

    Task<ArtistHandle> EnsureArtistAsync(LibraryHandle library, string name, string? sortName, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? bannerBytes = null, string? bannerMimeType = null, byte[]? clearLogoBytes = null, string? clearLogoMimeType = null);
    Task<AlbumHandle> EnsureAlbumAsync(LibraryHandle library, ArtistHandle artist, string title, int? year, string? genre, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? discArtBytes = null, string? discArtMimeType = null, string? albumArtist = null, bool isCompilation = false, IReadOnlyList<string>? albumFolders = null);
    Task<MediaItemHandle> EnsureTrackAsync(LibraryHandle library, AlbumHandle album, string title, int trackNumber, int? discNumber, int? durationSeconds, string? audioCodec, int? sampleRate, int? bitrate, string? contentRating, string? trackArtist = null, string? isrc = null);

    Task AddMediaPartAsync(MediaItemHandle item, string filePath, string? resolution, string? edition = null, bool syncEdition = true);

    Task AttachLocalExtraAsync(LibraryHandle library, string parentTitle, int? parentYear, string filePath, string extraType, string title);
    Task AttachTvShowLocalExtraAsync(LibraryHandle library, string showTitle, string filePath, string extraType, string title);
}
