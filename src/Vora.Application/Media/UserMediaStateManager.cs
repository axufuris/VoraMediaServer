using Vora.Application.Analysis;
using Vora.Application.Media.ViewModels;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Media;

public interface IUserMediaStateManager
{
    Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId);
    Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed);
}

public class UserMediaStateManager : IUserMediaStateManager
{
    private readonly IUserMediaStateRepository _repository;
    private readonly IClientNotifier _notifier;

    public UserMediaStateManager(IUserMediaStateRepository repository, IClientNotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId)
    {
        return await _repository.GetUpNextAsync(mediaId, contextType, contextId);
    }

    public async Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed)
    {
        await _repository.SetMediaPlayedStateAsync(mediaItemId, profileId, isPlayed);

        await _notifier.NotifyMediaItemUpdatedAsync(mediaItemId);
    }
}
