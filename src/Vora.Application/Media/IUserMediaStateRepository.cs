using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media.ViewModels;
using Vora.Application.Sync.ViewModels;

namespace Vora.Application.Media;

public interface IUserMediaStateRepository
{
    Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId);
    Task AttachUserMediaStateAsync(MediaDetailsVM vm, Guid profileId);
    Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed);
    Task AttachLibraryItemUserStatesAsync(IEnumerable<LibraryItemVM> items, Guid profileId);
    Task<List<ContinueWatchingVM>> GetContinueWatchingAsync(Guid profileId, int limit = 15);
    Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId);

    Task<Dictionary<Guid, decimal>> GetMediaRatingsAsync(Guid profileId, IEnumerable<Guid> mediaItemIds);
    Task<SetMediaRatingResult> SetMediaRatingAsync(Guid profileId, Guid mediaItemId, decimal? rating, bool isAdmin);
}

public sealed class SetMediaRatingResult
{
    public bool Found { get; init; }
    public bool ServerAdminRatingChanged { get; init; }
}
