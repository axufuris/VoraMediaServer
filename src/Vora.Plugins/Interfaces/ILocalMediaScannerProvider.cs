namespace Vora.Plugins.Interfaces;

public interface ILocalMediaScannerProvider : IVoraPlugin
{
    Task ScanMovieLibraryAsync(Guid libraryId);
    Task ScanTvShowLibraryAsync(Guid libraryId);
    Task ScanMusicLibraryAsync(Guid libraryId);
    Task ScanMovieAsync(Guid movieId);
    Task ScanTvShowAsync(Guid tvShowId);
    Task ScanSeasonAsync(Guid seasonId);
    Task ScanEpisodeAsync(Guid episodeId);
}
