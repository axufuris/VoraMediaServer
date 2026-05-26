namespace Vora.Application.Thumbnails;

public interface IVideoThumbnailManager
{
    Task TriggerMediaItemThumbnailGenerationAsync(Guid mediaItemId, bool forceOverride = false, bool isScheduleTrigger = false);
    Task TriggerLibraryThumbnailGenerationAsync(Guid libraryId, bool forceOverride = false, bool isScheduleTrigger = false);
    Task<(int Total, int WithThumbnails)> GetCoverageAsync(Guid libraryId);
    Task PurgeLibraryThumbnailsAsync(Guid libraryId);
}
