using Microsoft.EntityFrameworkCore;
using Vora.Application.Discovery;
using Vora.Domain.Entities.Discovery;

namespace Vora.Infrastructure.Persistence.Repositories;

public class DiscoveryRepository(VoraDbContext context) : IDiscoveryRepository
{
    public async Task<List<DiscoveryRowConfig>> GetRowConfigsAsync() =>
        await context.DiscoveryRowConfigs
            .AsNoTracking()
            .OrderBy(r => r.OrderIndex)
            .ToListAsync();

    public async Task UpdateRowConfigsAsync(List<DiscoveryRowConfig> configs)
    {
        var existing = await context.DiscoveryRowConfigs.ToListAsync();
        context.DiscoveryRowConfigs.RemoveRange(existing);
        await context.DiscoveryRowConfigs.AddRangeAsync(configs);
        await context.SaveChangesAsync();
    }

    public async Task<List<UserWatchlistItem>> GetWatchlistAsync(Guid profileId) =>
        await context.UserWatchlistItems
            .AsNoTracking()
            .Where(w => w.ProfileId == profileId)
            .OrderByDescending(w => w.AddedAt)
            .ToListAsync();

    public async Task AddToWatchlistAsync(UserWatchlistItem item)
    {
        var exists = await context.UserWatchlistItems
            .AnyAsync(w => w.ProfileId == item.ProfileId && w.ExternalId == item.ExternalId && w.ProviderId == item.ProviderId);

        if (exists)
        {
            return;
        }

        await context.UserWatchlistItems.AddAsync(item);
        await context.SaveChangesAsync();
    }

    public async Task RemoveFromWatchlistAsync(Guid profileId, string externalId, string providerId)
    {
        var item = await context.UserWatchlistItems
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && w.ExternalId == externalId && w.ProviderId == providerId);

        if (item == null)
        {
            return;
        }

        context.UserWatchlistItems.Remove(item);
        await context.SaveChangesAsync();
    }

    public Task<bool> IsInWatchlistAsync(Guid profileId, string externalId, string providerId) =>
        context.UserWatchlistItems.AnyAsync(w => w.ProfileId == profileId && w.ExternalId == externalId && w.ProviderId == providerId);
}
