using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Discovery;

public interface IDiscoveryRepository
{
    Task<List<DiscoveryRowConfig>> GetRowConfigsAsync();
    Task UpdateRowConfigsAsync(List<DiscoveryRowConfig> configs);

    Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId);
    Task AddToWatchlistAsync(UserWatchlistItem item);
    Task RemoveFromWatchlistAsync(Guid profileId, string externalId, string providerId);
    Task<bool> IsInWatchlistAsync(Guid profileId, string externalId, string providerId);
}