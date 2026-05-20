using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface ICollectionSyncProvider : IVoraPlugin
{
    string ExternalIdLabel { get; }
    string ExternalIdPlaceholder { get; }

    Task<List<CollectionSyncItemDto>> FetchItemsAsync(string externalId);
}
