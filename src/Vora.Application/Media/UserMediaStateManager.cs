using Vora.Application.Analysis;
using Vora.Application.Media.ViewModels;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IUserMediaStateManager
{
    Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId);
    Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed);
    Task<SetMediaRatingResult> SetMediaRatingAsync(Guid mediaItemId, Guid profileId, decimal? rating, bool isAdmin);
}

public class UserMediaStateManager : IUserMediaStateManager
{
    private readonly IUserMediaStateRepository _repository;
    private readonly IClientNotifier _notifier;
    private readonly Vora.Application.Tasks.ITaskQueueManager _taskQueueManager;

    public UserMediaStateManager(IUserMediaStateRepository repository, IClientNotifier notifier, Vora.Application.Tasks.ITaskQueueManager taskQueueManager)
    {
        _repository = repository;
        _notifier = notifier;
        _taskQueueManager = taskQueueManager;
    }

    public async Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId)
    {
        return await _repository.GetUpNextAsync(mediaId, contextType, contextId);
    }

    public async Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed)
    {
        await _repository.SetMediaPlayedStateAsync(mediaItemId, profileId, isPlayed);

        await _notifier.NotifyMediaItemUpdatedAsync(mediaItemId);
        await _notifier.NotifyUserMediaStateUpdatedAsync(profileId);
    }

    public async Task<SetMediaRatingResult> SetMediaRatingAsync(Guid mediaItemId, Guid profileId, decimal? rating, bool isAdmin)
    {
        if (rating.HasValue && (rating.Value < 0m || rating.Value > 10m))
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 10.");
        }

        var result = await _repository.SetMediaRatingAsync(profileId, mediaItemId, rating, isAdmin);
        if (!result.Found) return result;

        await _notifier.NotifyMediaItemUpdatedAsync(mediaItemId);

        if (result.ServerAdminRatingChanged)
        {
            _taskQueueManager.QueueGeneratePosterOverlays(mediaItemId);
        }

        return result;
    }
}
