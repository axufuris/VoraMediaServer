using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Domain.Entities.Collections;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public class CollectionSyncService(
    ICollectionRepository collectionRepo,
    IMediaRepository mediaRepo,
    IEnumerable<ICollectionSyncProvider> providers,
    IClientNotifier notifier,
    ILogger<CollectionSyncService> logger)
{
    public async Task SyncCollectionContentAsync(Guid collectionId)
    {
        var collection = await collectionRepo.GetProjectedByIdAsync(collectionId, c => new
        {
            c.Id,
            c.Title,
            c.ContentSyncProviderId,
            c.ContentSyncExternalId
        });

        if (collection == null
            || string.IsNullOrEmpty(collection.ContentSyncProviderId)
            || string.IsNullOrEmpty(collection.ContentSyncExternalId))
        {
            return;
        }

        var provider = providers.FirstOrDefault(p => p.Id == collection.ContentSyncProviderId);
        if (provider == null)
        {
            logger.LogWarning("Collection Sync Provider '{ProviderId}' not found.", collection.ContentSyncProviderId);
            return;
        }

        try
        {
            var externalItems = await provider.FetchItemsAsync(collection.ContentSyncExternalId);
            if (externalItems == null || !externalItems.Any())
            {
                return;
            }

            var tmdbIds = externalItems.Where(x => !string.IsNullOrEmpty(x.TmdbId)).Select(x => x.TmdbId!).ToList();
            var imdbIds = externalItems.Where(x => !string.IsNullOrEmpty(x.ImdbId)).Select(x => x.ImdbId!).ToList();

            var matchingLocalMediaIds = await mediaRepo.GetMediaIdsByExternalIdsAsync(tmdbIds, imdbIds);
            if (matchingLocalMediaIds.Count == 0)
            {
                return;
            }

            var existingMediaIds = await collectionRepo.GetCollectionMediaIdsAsync(collection.Id);

            var itemsToAdd = matchingLocalMediaIds
                .Where(id => !existingMediaIds.Contains(id))
                .Select(id => new CollectionItem
                {
                    CollectionId = collection.Id,
                    MediaItemId = id
                })
                .ToList();

            if (itemsToAdd.Count == 0)
            {
                return;
            }

            await collectionRepo.AddItemsToCollectionAsync(itemsToAdd);
            logger.LogInformation("Auto-synced {Count} new items to collection '{Title}'.", itemsToAdd.Count, collection.Title);

            await notifier.NotifyCollectionUpdatedAsync(collection.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync content for collection '{Title}'.", collection.Title);
        }
    }
}
