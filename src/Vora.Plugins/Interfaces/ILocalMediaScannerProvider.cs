using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ILocalMediaScannerProvider : IVoraPlugin
{
    Task ScanMovieLibraryAsync(Guid libraryId);
    Task ScanTvShowLibraryAsync(Guid libraryId);
    Task ScanMusicLibraryAsync(Guid libraryId);

    // Discovery + per-unit ingest, so the workflow can scan and enrich each
    // show/movie as an isolated unit (in parallel). A "unit" is one show (all
    // its episode + extra files) or one movie (its file(s) + extras).
    Task<List<ScanUnit>> DiscoverMovieScanUnitsAsync(Guid libraryId);
    Task<List<ScanUnit>> DiscoverTvScanUnitsAsync(Guid libraryId);
    Task<Guid?> ScanMovieUnitAsync(Guid libraryId, IReadOnlyList<string> filePaths);
    Task<Guid?> ScanTvUnitAsync(Guid libraryId, IReadOnlyList<string> filePaths);
    Task ScanMovieAsync(Guid movieId);
    Task ScanTvShowAsync(Guid tvShowId);
    Task ScanSeasonAsync(Guid seasonId);
    Task ScanEpisodeAsync(Guid episodeId);

    // Single-file ingest for the folder watcher — process just the added file
    // instead of rescanning the whole library.
    Task<Guid?> ScanMovieFileAsync(Guid libraryId, string filePath);
    Task<ScanFileResult> ScanTvFileAsync(Guid libraryId, string filePath);
}
