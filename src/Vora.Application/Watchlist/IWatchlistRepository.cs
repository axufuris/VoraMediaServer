using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Watchlist;

public interface IWatchlistRepository
{
    Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId);
    Task<UserWatchlistItem?> FindAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId);
    Task AddAsync(UserWatchlistItem item);
    Task RemoveAsync(UserWatchlistItem item);
    Task SetMediaItemIdAsync(Guid watchlistItemId, Guid mediaItemId);
}
