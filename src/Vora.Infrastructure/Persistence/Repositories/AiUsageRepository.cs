using Microsoft.EntityFrameworkCore;
using Vora.Application.Ai;
using Vora.Domain.Entities.Ai;

namespace Vora.Infrastructure.Persistence.Repositories;

public class AiUsageRepository(VoraDbContext context) : IAiUsageRepository
{
    public async Task LogAiUsageAsync(AiUsageLog log)
    {
        await context.AiUsageLogs.AddAsync(log);
        await context.SaveChangesAsync();
    }

    public Task<long> GetMonthlyTokenUsageAsync()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return context.AiUsageLogs
            .Where(l => l.Timestamp >= startOfMonth)
            .SumAsync(l => (long)l.TotalTokens);
    }
}
