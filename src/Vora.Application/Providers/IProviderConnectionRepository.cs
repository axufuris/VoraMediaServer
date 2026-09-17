using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Providers;

public interface IProviderConnectionRepository
{
    Task<IEnumerable<T>> GetProjectedUserConnectionsAsync<T>(Guid userId, Expression<Func<UserProviderConnection, T>> projection);
    Task<UserProviderConnection?> GetConnectionAsync(Guid userId, string providerName);
    Task<IEnumerable<UserProviderConnection>> GetUserConnectionsAsync(Guid userId);
    Task AddConnectionAsync(UserProviderConnection connection);
    Task UpdateConnectionAsync(UserProviderConnection connection);
    Task RemoveConnectionAsync(UserProviderConnection connection);
}