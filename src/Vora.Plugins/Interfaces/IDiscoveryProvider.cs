using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IDiscoveryProvider : IVoraPlugin
{
    Task<IEnumerable<DiscoveryRowDefinitionDto>> GetAvailableRowsAsync();

    Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string rowId, int page = 1);

    Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string externalId, string type);

    Task<DiscoveryActorDto?> GetActorDetailsAsync(string externalId);

    Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query);
}
