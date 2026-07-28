using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ILocalMediaScannerProvider : IVoraPlugin
{
    Task ScanMovieLibraryAsync(Guid libraryId, Func<Guid, Task>? onMovieScannedAsync = null);
    Task ScanTvShowLibraryAsync(Guid libraryId, Func<Guid, Task>? onShowScannedAsync = null);
    Task ScanMusicLibraryAsync(Guid libraryId);
    Task ScanMovieAsync(Guid movieId);
    Task ScanTvShowAsync(Guid tvShowId);
    Task ScanSeasonAsync(Guid seasonId);
    Task ScanEpisodeAsync(Guid episodeId);

    // Single-file ingest for the folder watcher — process just the added file
    // instead of rescanning the whole library.
    Task<Guid?> ScanMovieFileAsync(Guid libraryId, string filePath);
    Task<ScanFileResult> ScanTvFileAsync(Guid libraryId, string filePath);
}
