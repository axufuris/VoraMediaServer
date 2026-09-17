using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Application.Media;

public interface IMediaDedupeRepository
{
    Task<TvShowMergeResultVM> MergeDuplicateTvShowsAsync(Guid? libraryId);
    Task<List<MediaItem>> GetMediaItemsWithMultiplePartsAsync();
    Task<MediaPart?> GetMediaPartByIdAsync(Guid partId);
    Task DeleteMediaPartAsync(MediaPart part);

    Task<MediaDedupeSettings?> GetGlobalSettingsAsync();
    Task<MediaDedupeSettings?> GetLibraryOverrideAsync(Guid libraryId);
    Task<List<MediaDedupeSettings>> GetAllLibraryOverridesAsync();
    Task<MediaDedupeSettings> UpsertSettingsAsync(MediaDedupeSettings settings);
    Task DeleteLibraryOverrideAsync(Guid libraryId);

    Task<List<MediaDedupeIgnoredGroup>> GetIgnoredGroupsAsync();
    Task<MediaDedupeIgnoredGroup?> GetIgnoredGroupAsync(Guid mediaItemId, string resolution);
    Task AddIgnoredGroupAsync(MediaDedupeIgnoredGroup group);
    Task RemoveIgnoredGroupAsync(Guid ignoredGroupId);
}
