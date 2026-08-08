using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.Dtos;
using Vora.Application.Tasks;

namespace Vora.Application.Collections;

public class CollectionMembershipService(
    ICollectionRepository collectionRepo,
    IMediaRepository mediaRepo,
    ITaskQueueManager taskQueue,
    IClientNotifier notifier,
    ILogger<CollectionMembershipService> logger)
{
    // When a new movie/show lands in the library, slot it into any content-synced
    // collection whose remembered desired-membership already lists it — without
    // re-fetching the external list or calling AI. This closes the gap where a
    // collection couldn't pick up an item that was added to the library after the
    // collection was last synced.
    public async Task CheckMediaItemForCollectionsAsync(Guid mediaItemId)
    {
        var info = await mediaRepo.GetMediaMatchInfoAsync(mediaItemId);
        if (info == null)
        {
            return;
        }

        var memberships = await collectionRepo.GetContentSyncMembershipsAsync();
        if (memberships.Count == 0)
        {
            return;
        }

        var normalizedTitle = TitleMatch.Normalize(info.Title);

        foreach (var membership in memberships)
        {
            if (string.IsNullOrWhiteSpace(membership.ContentSyncCacheJson))
            {
                continue;
            }

            List<CollectionMembershipEntry>? entries;
            try
            {
                entries = JsonSerializer.Deserialize<List<CollectionMembershipEntry>>(membership.ContentSyncCacheJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entries == null || !entries.Any(e => Matches(e, info, normalizedTitle)))
            {
                continue;
            }

            var existing = await collectionRepo.GetCollectionMediaIdsAsync(membership.Id);
            if (existing.Contains(mediaItemId))
            {
                continue;
            }

            await collectionRepo.AddItemToCollectionAsync(membership.Id, mediaItemId);
            await notifier.NotifyCollectionUpdatedAsync(membership.Id);
            taskQueue.QueueReevaluateCollectionOrder(membership.Id);
            logger.LogInformation("Auto-added new library item {MediaItemId} to collection {CollectionId} from remembered membership.", mediaItemId, membership.Id);
        }
    }

    private static bool Matches(CollectionMembershipEntry entry, MediaMatchInfoDto info, string normalizedTitle)
    {
        if (!string.IsNullOrEmpty(entry.TmdbId) && entry.TmdbId == info.TmdbId)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(entry.ImdbId) && entry.ImdbId == info.ImdbId)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(entry.Title)
            && TitleMatch.Normalize(entry.Title) == normalizedTitle
            && string.Equals(entry.MediaType, info.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            return entry.Year == null || info.Year == null || entry.Year == info.Year;
        }

        return false;
    }
}
