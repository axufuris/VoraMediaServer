using Microsoft.EntityFrameworkCore;
using Vora.Application.Webhooks;
using Vora.Domain.Entities.Webhooks;

namespace Vora.Infrastructure.Persistence.Repositories;

public class WebhookConnectionRepository(VoraDbContext context) : IWebhookConnectionRepository
{
    public Task<List<WebhookConnection>> GetAllAsync() =>
        context.WebhookConnections.AsNoTracking().ToListAsync();

    public Task<WebhookConnection?> GetAsync(Guid id) =>
        context.WebhookConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

    public async Task AddAsync(WebhookConnection connection)
    {
        context.WebhookConnections.Add(connection);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(WebhookConnection connection)
    {
        context.WebhookConnections.Update(connection);
        await context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var connection = await context.WebhookConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (connection == null) return false;

        context.WebhookConnections.Remove(connection);
        await context.SaveChangesAsync();
        return true;
    }
}
