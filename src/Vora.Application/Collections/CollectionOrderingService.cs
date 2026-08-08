using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Domain.Entities.Media;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public class CollectionOrderingService(
    ICollectionRepository repository,
    Vora.Application.Media.IMediaRepository mediaRepository,
    IEnumerable<IChronologyProvider> providers,
    IClientNotifier notifier,
    ILogger<CollectionOrderingService> logger)
{
    public async Task ApplyChronologicalOrderAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var config = await repository.GetChronologyConfigAsync(collectionId);

        if (config == null || string.IsNullOrEmpty(config.SortProviderId))
        {
            return;
        }

        var provider = providers.FirstOrDefault(p => p.Id == config.SortProviderId)
            ?? throw new InvalidOperationException($"Chronology provider '{config.SortProviderId}' not found.");

        try
        {
            var collectionItems = await repository.GetCollectionItemsWithMediaAsync(collectionId);
            var signature = ComputeSignature(collectionItems.Select(i => i.MediaItemId));

            // A provider that orders the collection's own items (AI) yields the
            // same result while the item set is unchanged, so skip the paid call.
            if (provider.OrdersLocalItemsOnly && signature == config.ChronologyItemsSignature)
            {
                await repository.TouchChronologySyncedAtAsync(collectionId);
                return;
            }

            var seasonInfo = await mediaRepository.GetSeasonShowInfoAsync(
                collectionItems.Where(i => i.MediaItem is Season).Select(i => i.MediaItemId).ToList());

            var orderingItems = collectionItems
                .Select((i, idx) => new CollectionOrderingItemDto
                {
                    Index = idx,
                    LocalId = i.MediaItemId,
                    Title = i.MediaItem.Title,
                    Year = i.MediaItem.ReleaseDate?.Year,
                    MediaType = i.MediaItem is Movie ? "Movie"
                        : i.MediaItem is Season ? "Season"
                        : i.MediaItem is TvShow ? "TvShow"
                        : "Movie",
                    TmdbId = i.MediaItem.TmdbId,
                    ImdbId = i.MediaItem.ImdbId,
                    ShowTitle = seasonInfo.TryGetValue(i.MediaItemId, out var si) ? si.ShowTitle : null,
                    SeasonNumber = seasonInfo.TryGetValue(i.MediaItemId, out var sn) ? sn.SeasonNumber : (int?)null
                })
                .ToList();

            var remoteOrder = await provider.GetChronologicalOrderAsync(config.Title, config.ExternalListId, orderingItems, cancellationToken);

            foreach (var item in collectionItems)
            {
                var match = remoteOrder.FirstOrDefault(r =>
                    (r.LocalId != null && r.LocalId == item.MediaItemId)
                    || (!string.IsNullOrEmpty(r.TmdbId) && r.TmdbId == item.MediaItem.TmdbId)
                    || (!string.IsNullOrEmpty(r.ImdbId) && r.ImdbId == item.MediaItem.ImdbId));

                if (match != null)
                {
                    item.SortOrder = match.SortOrder;
                }
            }

            await repository.UpdateCollectionItemsAsync(collectionItems);
            await repository.UpdateChronologySignatureAsync(collectionId, signature);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply chronological order for collection {CollectionId}.", collectionId);
            throw;
        }
    }

    public async Task ReevaluateOrderOnItemAddedAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var config = await repository.GetChronologyConfigAsync(collectionId);
        if (config == null || string.IsNullOrEmpty(config.SortProviderId))
        {
            return;
        }

        // Only re-run the ordering provider when the collection's item set has
        // actually changed since the last order. Unchanged set → the previous
        // order still holds, so skip the (often remote) provider call. The
        // manual "Sync Timeline" path calls ApplyChronologicalOrderAsync
        // directly and is never gated, so remote list re-orders can still be
        // pulled on demand.
        var currentSignature = ComputeSignature(await repository.GetCollectionMediaIdsAsync(collectionId));
        if (currentSignature == config.ChronologyItemsSignature)
        {
            return;
        }

        await ApplyChronologicalOrderAsync(collectionId, cancellationToken);
        await notifier.NotifyCollectionUpdatedAsync(collectionId);
    }

    private static string ComputeSignature(IEnumerable<Guid> mediaItemIds)
    {
        var joined = string.Join(",", mediaItemIds.OrderBy(x => x));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }
}
