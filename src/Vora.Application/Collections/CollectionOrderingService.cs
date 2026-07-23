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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply chronological order for collection {CollectionId}.", collectionId);
            throw;
        }
    }

    public async Task ReevaluateOrderOnItemAddedAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var sortProviderId = await repository.GetProjectedByIdAsync(collectionId, c => c.SortProviderId);
        if (string.IsNullOrEmpty(sortProviderId))
        {
            return;
        }

        await ApplyChronologicalOrderAsync(collectionId, cancellationToken);
        await notifier.NotifyCollectionUpdatedAsync(collectionId);
    }
}
