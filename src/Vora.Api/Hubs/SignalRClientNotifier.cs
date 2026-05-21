using Microsoft.AspNetCore.SignalR;
using Vora.Application.Analysis;
using Vora.Application.Logging.ViewModels;

namespace Vora.Api.Hubs;

public class SignalRClientNotifier(IHubContext<VoraHub> hubContext) : IClientNotifier
{
    public Task NotifyLogEntriesAsync(IReadOnlyList<LogEntryVM> entries) =>
        hubContext.Clients.Group("admins").SendAsync("LogEntryBatch", entries);

    public Task NotifyCollectionUpdatedAsync(Guid collectionId) =>
        hubContext.Clients.All.SendAsync("CollectionUpdated", collectionId);

    public Task NotifyLibraryUpdatedAsync(Guid libraryId) =>
        hubContext.Clients.All.SendAsync("LibraryUpdated", libraryId);

    public Task NotifyMediaItemUpdatedAsync(Guid mediaItemId) =>
        hubContext.Clients.All.SendAsync("MediaItemUpdated", mediaItemId);

    public Task NotifyMediaAnalysisUpdatedAsync(Guid mediaItemId) =>
        hubContext.Clients.All.SendAsync("MediaAnalysisUpdated", mediaItemId);

    public Task NotifySmartListsUpdatedAsync() =>
        hubContext.Clients.All.SendAsync("SmartListsUpdated");

    public Task NotifyTasksUpdatedAsync() =>
        hubContext.Clients.All.SendAsync("TasksUpdated");

    public Task NotifyUserAccessUpdatedAsync(Guid userId) =>
        hubContext.Clients.All.SendAsync("UserAccessUpdated", userId.ToString());

    public Task NotifyProfileAccessUpdatedAsync(Guid profileId) =>
        hubContext.Clients.All.SendAsync("ProfileAccessUpdated", profileId.ToString());

    public Task NotifyDvrSessionsUpdatedAsync() =>
        hubContext.Clients.All.SendAsync("DvrSessionsUpdated");

    public Task NotifyPodcastEpisodesUpdatedAsync(Guid showId) =>
        hubContext.Clients.All.SendAsync("PodcastEpisodesUpdated", showId.ToString());

    public Task NotifyMusicArtistUpdatedAsync(Guid artistId) =>
        hubContext.Clients.All.SendAsync("MusicArtistUpdated", artistId.ToString());

    public Task NotifyMusicAlbumUpdatedAsync(Guid albumId) =>
        hubContext.Clients.All.SendAsync("MusicAlbumUpdated", albumId.ToString());

    public Task NotifyMusicMixesUpdatedAsync(Guid profileId) =>
        hubContext.Clients.All.SendAsync("MusicMixesUpdated", profileId.ToString());

    public Task NotifyServerPlaybackUpdatedAsync() =>
        hubContext.Clients.All.SendAsync("ServerPlaybackUpdated");

    public Task NotifyAdminAlertAsync(string severity, string title, string message) =>
        hubContext.Clients.Group("admins").SendAsync("AdminAlert", new { severity, title, message, timestamp = DateTime.UtcNow });

    public Task NotifyAdminAlertUnreadChangedAsync() =>
        hubContext.Clients.Group("admins").SendAsync("AdminAlertUnreadChanged");

    public Task NotifyAdminThemeChangedAsync(string themeId) =>
        hubContext.Clients.All.SendAsync("AdminThemeChanged", themeId);

    public Task NotifyClientTemplateConfigurationChangedAsync() =>
        hubContext.Clients.All.SendAsync("ClientTemplateConfigurationChanged");

    public Task NotifyBackupCreatedAsync(string fileName) =>
        hubContext.Clients.Group("admins").SendAsync("BackupCreated", fileName);

    public Task NotifyBackupRestoredAsync(string fileName, IReadOnlyList<string> restoredSectionKeys) =>
        hubContext.Clients.Group("admins").SendAsync("BackupRestored", new { fileName, sectionKeys = restoredSectionKeys });
}
