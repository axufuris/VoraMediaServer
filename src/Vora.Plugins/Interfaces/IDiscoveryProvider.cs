using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IDiscoveryProvider : IVoraPlugin
{
    Task<IEnumerable<DiscoveryRowDefinitionDto>> GetAvailableRowsAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string rowId, int page = 1, CancellationToken cancellationToken = default);

    Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string externalId, string type, CancellationToken cancellationToken = default);

    Task<DiscoveryActorDto?> GetActorDetailsAsync(string externalId, CancellationToken cancellationToken = default);

    Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
