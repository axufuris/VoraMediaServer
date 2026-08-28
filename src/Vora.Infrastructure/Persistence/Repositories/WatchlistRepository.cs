using Microsoft.EntityFrameworkCore;
using Vora.Application.Watchlist;
using Vora.Domain.Entities.Discovery;

namespace Vora.Infrastructure.Persistence.Repositories;

public class WatchlistRepository(VoraDbContext context) : IWatchlistRepository
{
    public async Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId) =>
        await context.UserWatchlistItems
            .AsNoTracking()
            .Where(w => w.ProfileId == profileId)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();

    // Matches on either half of the identity, so a row added from Discovery is
    // found when the same title is toggled from the library and vice versa.
    public Task<UserWatchlistItem?> FindAsync(Guid profileId, string? externalId, string? providerId, Guid? mediaItemId)
    {
        var hasExternal = !string.IsNullOrWhiteSpace(externalId) && !string.IsNullOrWhiteSpace(providerId);
        if (!hasExternal && mediaItemId == null) return Task.FromResult<UserWatchlistItem?>(null);

        return context.UserWatchlistItems.FirstOrDefaultAsync(w =>
            w.ProfileId == profileId
            && ((hasExternal && w.ExternalId == externalId && w.ProviderId == providerId)
                || (mediaItemId != null && w.MediaItemId == mediaItemId)));
    }

    public async Task AddAsync(UserWatchlistItem item)
    {
        await context.UserWatchlistItems.AddAsync(item);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(UserWatchlistItem item)
    {
        context.UserWatchlistItems.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task SetMediaItemIdAsync(Guid watchlistItemId, Guid mediaItemId) =>
        await context.UserWatchlistItems
            .Where(w => w.Id == watchlistItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.MediaItemId, mediaItemId));
}
