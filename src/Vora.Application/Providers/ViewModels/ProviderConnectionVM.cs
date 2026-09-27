using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Providers.ViewModels;

public class ProviderConnectionVM
{
    public Guid Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public bool IsExpired { get; set; }

    public static Expression<Func<UserProviderConnection, ProviderConnectionVM>> Projection =>
        c => new ProviderConnectionVM
        {
            Id = c.Id,
            ProviderName = c.ProviderName,
            ConnectedAt = c.ConnectedAt,
            IsExpired = c.ExpiresAt.HasValue && c.ExpiresAt.Value < DateTime.UtcNow
        };
}
