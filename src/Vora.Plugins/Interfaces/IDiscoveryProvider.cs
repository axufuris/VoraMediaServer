using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IDiscoveryProvider : IVoraPlugin
{
    // False until the admin has entered whatever the source needs, the same
    // contract ISubtitleSearchProvider carries. FeatureFlagsVM.Discover is
    // derived from it, and the clients hide the Discover nav entry when nothing
    // reports ready — a provider with no key returns no rows at all, so an
    // unconfigured one must answer false rather than leaving an empty page in
    // the navigation.
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<DiscoveryRowDefinitionDto>> GetAvailableRowsAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<DiscoveryItemDto>> GetRowItemsAsync(string rowId, int page = 1, CancellationToken cancellationToken = default);

    Task<DiscoveryItemDetailsDto?> GetItemDetailsAsync(string externalId, string type, CancellationToken cancellationToken = default);

    Task<DiscoveryActorDto?> GetActorDetailsAsync(string externalId, CancellationToken cancellationToken = default);

    Task<IEnumerable<DiscoveryItemDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
