using Microsoft.Extensions.Logging;
using Vora.Application.Media;
using Vora.Application.Sync.ViewModels;

namespace Vora.Application.Sync;

public interface ISyncAndStateManager
{
    Task<IEnumerable<ContinueWatchingVM>> GetContinueWatchingFeedAsync(Guid profileId, int limit = 10);
    Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId);
}

public class SyncAndStateManager(IUserMediaStateRepository repository, ILogger<SyncAndStateManager> logger) : ISyncAndStateManager
{
    public async Task<IEnumerable<ContinueWatchingVM>> GetContinueWatchingFeedAsync(Guid profileId, int limit = 10) =>
        await repository.GetContinueWatchingAsync(profileId, limit);

    public async Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId)
    {
        try
        {
            await repository.HideFromContinueWatchingAsync(profileId, mediaItemId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to hide media {MediaItemId} from continue-watching for profile {ProfileId}", mediaItemId, profileId);
            throw;
        }
    }
}
