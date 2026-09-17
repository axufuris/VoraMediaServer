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
}
