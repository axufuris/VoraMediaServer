using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Notifications;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public class CollectionSyncService(
    ICollectionRepository collectionRepo,
    IMediaRepository mediaRepo,
    IEnumerable<ICollectionSyncProvider> providers,
    IClientNotifier notifier,
    ITaskQueueManager taskQueue,
    IAdminNotificationManager adminNotifications,
    ILogger<CollectionSyncService> logger)
{
    public async Task SyncCollectionContentAsync(Guid collectionId)
    {
        var collection = await collectionRepo.GetProjectedByIdAsync(collectionId, c => new
        {
            c.Id,
            c.Title,
            c.Description,
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
            var existingMediaIds = await collectionRepo.GetCollectionMediaIdsAsync(collection.Id);
            var collectionIsEmpty = existingMediaIds.Count == 0;

            var externalItems = await provider.FetchItemsAsync(collection.ContentSyncExternalId);
            if (externalItems == null || !externalItems.Any())
            {
                if (collectionIsEmpty)
                {
                    await adminNotifications.RaiseAsync(AdminNotificationSeverity.Warning,
                        $"'{collection.Title}' got no titles from the AI",
                        "The AI List works best for a specific franchise or shared universe (e.g. \"Marvel Cinematic Universe\"). A broad genre or mood (e.g. \"kung fu movies\") often returns nothing — build those with a Smart Playlist instead.");
                }
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

            if (string.IsNullOrWhiteSpace(collection.Description))
            {
                var generatedDescription = await provider.GenerateDescriptionAsync(collection.ContentSyncExternalId);
                if (!string.IsNullOrWhiteSpace(generatedDescription))
                {
                    await collectionRepo.UpdateDescriptionAsync(collection.Id, generatedDescription.Trim());
                    await notifier.NotifyCollectionUpdatedAsync(collection.Id);
                }
            }

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

            var excludedIds = await collectionRepo.GetExcludedMediaIdsAsync(collection.Id);
            matchedIds.ExceptWith(excludedIds);

            var matchingLocalMediaIds = matchedIds.ToList();
            if (matchingLocalMediaIds.Count == 0)
            {
                if (collectionIsEmpty)
                {
                    await adminNotifications.RaiseAsync(AdminNotificationSeverity.Warning,
                        $"'{collection.Title}' matched nothing in your library",
                        $"The AI listed {externalItems.Count} title(s) for this collection, but none matched an item in your library. Check the description names the right franchise, or that the titles are in a scanned library.");
                }
                return;
            }

            var manuallyAddedIds = await collectionRepo.GetManuallyAddedMediaIdsAsync(collection.Id);
            var membershipChanged = false;

            if (collection.MirrorList)
            {
                var desired = matchingLocalMediaIds.ToHashSet();
                var toRemove = existingMediaIds.Where(id => !desired.Contains(id) && !manuallyAddedIds.Contains(id)).ToList();
                if (toRemove.Count > 0)
                {
                    await collectionRepo.RemoveItemsFromCollectionAsync(collection.Id, toRemove);
                    await notifier.NotifyCollectionUpdatedAsync(collection.Id);
                    logger.LogInformation("Mirror sync removed {Count} item(s) no longer in the list from '{Title}'.", toRemove.Count, collection.Title);
                    membershipChanged = true;
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

            if (itemsToAdd.Count > 0)
            {
                await collectionRepo.AddItemsToCollectionAsync(itemsToAdd);
                logger.LogInformation("Auto-synced {Count} new items to collection '{Title}'.", itemsToAdd.Count, collection.Title);

                await notifier.NotifyCollectionUpdatedAsync(collection.Id);
                membershipChanged = true;
            }

            if (membershipChanged)
            {
                taskQueue.QueueReevaluateCollectionOrder(collection.Id);
            }
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
