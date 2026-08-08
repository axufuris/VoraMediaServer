using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public class CollectionOrderingService(
    ICollectionRepository repository,
    IEnumerable<IChronologyProvider> providers,
    IClientNotifier notifier,
    ILogger<CollectionOrderingService> logger)
{
    public async Task ApplyChronologicalOrderAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var config = await repository.GetProjectedByIdAsync(collectionId, c => new
        {
            c.Title,
            c.SortProviderId,
            c.ExternalListId
        });

        if (config == null || string.IsNullOrEmpty(config.SortProviderId))
        {
            return;
        }

        var provider = providers.FirstOrDefault(p => p.Id == config.SortProviderId)
            ?? throw new InvalidOperationException($"Chronology provider '{config.SortProviderId}' not found.");

        try
        {
            var remoteOrder = await provider.GetChronologicalOrderAsync(config.Title, config.ExternalListId, cancellationToken);
            var collectionItems = await repository.GetCollectionItemsWithMediaAsync(collectionId);

            foreach (var item in collectionItems)
            {
                var match = remoteOrder.FirstOrDefault(r =>
                    (!string.IsNullOrEmpty(r.TmdbId) && r.TmdbId == item.MediaItem.TmdbId)
                    || (!string.IsNullOrEmpty(r.ImdbId) && r.ImdbId == item.MediaItem.ImdbId));

                if (match != null)
                {
                    item.SortOrder = match.SortOrder;
                }
            }

            await repository.UpdateCollectionItemsAsync(collectionItems);
            await repository.UpdateChronologySignatureAsync(collectionId, ComputeSignature(collectionItems.Select(i => i.MediaItemId)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply chronological order for collection {CollectionId}.", collectionId);
            throw;
        }
    }

    public async Task ReevaluateOrderOnItemAddedAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var config = await repository.GetProjectedByIdAsync(collectionId, c => new { c.SortProviderId, c.ChronologyItemsSignature });
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
