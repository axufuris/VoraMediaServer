using Vora.Application.Logging.ViewModels;

namespace Vora.Application.Analysis;

public interface IClientNotifier
{
    Task NotifyLogEntriesAsync(IReadOnlyList<LogEntryVM> entries);
    Task NotifyCollectionUpdatedAsync(Guid collectionId);
    Task NotifyLibraryUpdatedAsync(Guid libraryId);
    Task NotifyMediaItemUpdatedAsync(Guid mediaItemId);
    Task NotifySmartListsUpdatedAsync();
    Task NotifyMediaAnalysisUpdatedAsync(Guid mediaItemId);
    Task NotifyTasksUpdatedAsync();
    Task NotifyUserAccessUpdatedAsync(Guid userId);
    Task NotifyProfileAccessUpdatedAsync(Guid profileId);
    Task NotifyDvrSessionsUpdatedAsync();
    Task NotifyPodcastEpisodesUpdatedAsync(Guid showId);
    Task NotifyMusicArtistUpdatedAsync(Guid artistId);
    Task NotifyMusicAlbumUpdatedAsync(Guid albumId);
    Task NotifyMusicMixesUpdatedAsync(Guid profileId);
    Task NotifyServerPlaybackUpdatedAsync();
    Task NotifyAdminAlertAsync(string severity, string title, string message);
    Task NotifyAdminAlertUnreadChangedAsync();
    Task NotifyAdminThemeChangedAsync(string themeId);
    Task NotifyClientTemplateConfigurationChangedAsync();
    Task NotifyBackupCreatedAsync(string fileName);
    Task NotifyBackupRestoredAsync(string fileName, IReadOnlyList<string> restoredSectionKeys);
}