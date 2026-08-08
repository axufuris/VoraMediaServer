using System.Text.Json;
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
            c.ContentSyncExternalId,
            c.MirrorList,
            c.LibraryId
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

            var membership = externalItems.Select(x => new CollectionMembershipEntry
            {
                TmdbId = x.TmdbId,
                ImdbId = x.ImdbId,
                MediaType = x.MediaType,
                Title = x.Title,
                Year = x.Year,
                ShowTitle = x.ShowTitle,
                SeasonNumber = x.SeasonNumber
            }).ToList();
            await collectionRepo.UpdateContentSyncCacheAsync(collection.Id, JsonSerializer.Serialize(membership));

            var tmdbIds = externalItems.Where(x => !string.IsNullOrEmpty(x.TmdbId)).Select(x => x.TmdbId!).ToList();
            var imdbIds = externalItems.Where(x => !string.IsNullOrEmpty(x.ImdbId)).Select(x => x.ImdbId!).ToList();

            var matchedIds = new HashSet<Guid>(await mediaRepo.GetMediaIdsByExternalIdsAsync(tmdbIds, imdbIds));

            if (membership.Any(NeedsTitleMatch))
            {
                var candidates = await mediaRepo.GetCollectionMatchCandidatesAsync(collection.LibraryId);
                foreach (var id in CollectionMembershipResolver.Resolve(membership.Where(NeedsTitleMatch), candidates))
                {
                    matchedIds.Add(id);
                }
            }

            var matchingLocalMediaIds = matchedIds.ToList();
            if (matchingLocalMediaIds.Count == 0)
            {
                return;
            }

            var existingMediaIds = await collectionRepo.GetCollectionMediaIdsAsync(collection.Id);

            // Mirror mode: drop items no longer in the list. Guarded by the
            // "no matches → return" above, so a total match failure can't wipe
            // the collection; a manual add to a mirrored collection is expected
            // to be removed on the next sync.
            if (collection.MirrorList)
            {
                var desired = matchingLocalMediaIds.ToHashSet();
                var toRemove = existingMediaIds.Where(id => !desired.Contains(id)).ToList();
                if (toRemove.Count > 0)
                {
                    await collectionRepo.RemoveItemsFromCollectionAsync(collection.Id, toRemove);
                    await notifier.NotifyCollectionUpdatedAsync(collection.Id);
                    logger.LogInformation("Mirror sync removed {Count} item(s) no longer in the list from '{Title}'.", toRemove.Count, collection.Title);
                }
            }

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

    private static bool NeedsTitleMatch(CollectionMembershipEntry entry) =>
        string.IsNullOrEmpty(entry.TmdbId)
        && string.IsNullOrEmpty(entry.ImdbId)
        && (!string.IsNullOrWhiteSpace(entry.Title) || !string.IsNullOrWhiteSpace(entry.ShowTitle));
}
