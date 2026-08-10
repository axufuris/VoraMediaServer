using Vora.Plugins.Dtos;

namespace Vora.Plugins.Interfaces;

public interface IChronologyProvider : IVoraPlugin
{
    string ProviderId { get; }

    string ExternalIdLabel { get; }
    string ExternalIdPlaceholder { get; }

    // True when the order is derived purely from the collection's own items
    // (e.g. AI sorting), so the caller can skip re-running it while the item
    // set is unchanged. False for providers whose order comes from an external
    // list that can change independently (Trakt/MDbList/IMDb).
    bool OrdersLocalItemsOnly => false;

    Task<List<ChronologyResult>> GetChronologicalOrderAsync(string collectionName, string? externalId = null, IReadOnlyList<CollectionOrderingItemDto>? items = null, CancellationToken cancellationToken = default);
}

public class ChronologyResult
{
    public Guid? LocalId { get; set; }
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string MediaType { get; set; } = "Movie";
    public decimal SortOrder { get; set; }
}
