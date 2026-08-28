using Vora.Domain.Entities.Discovery;

namespace Vora.Application.Discovery;

public interface IDiscoveryRepository
{
    Task<List<DiscoveryRowConfig>> GetRowConfigsAsync();
    Task UpdateRowConfigsAsync(List<DiscoveryRowConfig> configs);
}