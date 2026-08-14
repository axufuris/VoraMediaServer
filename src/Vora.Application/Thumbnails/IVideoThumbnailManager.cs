namespace Vora.Application.Thumbnails;

public interface IVideoThumbnailManager
{
    Task TriggerMediaItemThumbnailGenerationAsync(Guid mediaItemId, bool forceOverride = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default);
    Task TriggerLibraryThumbnailGenerationAsync(Guid libraryId, bool forceOverride = false, bool isScheduleTrigger = false, CancellationToken cancellationToken = default);
    Task GenerateForItemAsync(Guid mediaItemId, bool forceOverride, CancellationToken cancellationToken = default);
    Task<(int Total, int WithThumbnails)> GetCoverageAsync(Guid libraryId);
    Task PurgeLibraryThumbnailsAsync(Guid libraryId);
}
