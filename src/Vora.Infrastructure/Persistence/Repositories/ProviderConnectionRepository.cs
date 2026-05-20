using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Providers;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class ProviderConnectionRepository(VoraDbContext context) : IProviderConnectionRepository
{
    public async Task<IEnumerable<T>> GetProjectedUserConnectionsAsync<T>(Guid userId, Expression<Func<UserProviderConnection, T>> projection) =>
        await context.UserProviderConnections
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(projection)
            .ToListAsync();

    public Task<UserProviderConnection?> GetConnectionAsync(Guid userId, string providerName) =>
        context.UserProviderConnections.FirstOrDefaultAsync(x => x.UserId == userId && x.ProviderName == providerName);

    public async Task<IEnumerable<UserProviderConnection>> GetUserConnectionsAsync(Guid userId) =>
        await context.UserProviderConnections.Where(x => x.UserId == userId).ToListAsync();

    public async Task AddConnectionAsync(UserProviderConnection connection)
    {
        await context.UserProviderConnections.AddAsync(connection);
        await context.SaveChangesAsync();
    }

    public async Task UpdateConnectionAsync(UserProviderConnection connection)
    {
        context.UserProviderConnections.Update(connection);
        await context.SaveChangesAsync();
    }

    public async Task RemoveConnectionAsync(UserProviderConnection connection)
    {
        context.UserProviderConnections.Remove(connection);
        await context.SaveChangesAsync();
    }
}
