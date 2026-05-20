namespace Vora.Plugins.Interfaces;

public interface IMediaIngestionService
{
    Task<(List<string> FolderPaths, string? ScannerRegex)> GetLibraryDetailsAsync(Guid libraryId);
    Task<Guid?> GetLibraryIdForMediaAsync(Guid mediaItemId);
    Task<HashSet<string>> GetExistingLibraryPathsAsync(Guid libraryId);
    Task<List<string>> GetMediaFilePathsAsync(Guid mediaItemId);

    Task<Guid> EnsureMovieAsync(Guid libraryId, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null, string? edition = null);
    Task<Guid> EnsureTvShowAsync(Guid libraryId, string title, int? year, string? tmdbId, string? imdbId, string? tvdbId = null);
    Task<Guid> EnsureSeasonAsync(Guid libraryId, Guid tvShowId, int seasonNumber);
    Task<Guid> EnsureEpisodeAsync(Guid libraryId, Guid seasonId, int episodeNumber, string title, DateTime? airDate, string? edition = null);

    Task<Guid> EnsureArtistAsync(Guid libraryId, string name, string? sortName, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? bannerBytes = null, string? bannerMimeType = null, byte[]? clearLogoBytes = null, string? clearLogoMimeType = null);
    Task<Guid> EnsureAlbumAsync(Guid libraryId, Guid artistId, string title, int? year, string? genre, byte[]? artworkBytes, string? artworkMimeType, byte[]? backgroundBytes = null, string? backgroundMimeType = null, byte[]? discArtBytes = null, string? discArtMimeType = null, string? albumArtist = null, bool isCompilation = false);
    Task<Guid> EnsureTrackAsync(Guid libraryId, Guid albumId, string title, int trackNumber, int? discNumber, int? durationSeconds, string? audioCodec, int? sampleRate, int? bitrate, string? contentRating, string? trackArtist = null);

    Task AddMediaPartAsync(Guid mediaItemId, string filePath, string? resolution);
}
