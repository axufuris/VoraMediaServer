using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Media;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Notifications;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Enums;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Collections;

public class CollectionSyncService(
    ICollectionRepository collectionRepo,
    IMediaRepository mediaRepo,
    IEnumerable<ICollectionSyncProvider> providers,
    ISmartPlaylistEvaluator smartEvaluator,
    IClientNotifier notifier,
    ITaskQueueManager taskQueue,
    IAdminNotificationManager adminNotifications,
    ILogger<CollectionSyncService> logger)
{
    private static readonly JsonSerializerOptions SmartRuleJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task SyncCollectionContentAsync(Guid collectionId, CancellationToken cancellationToken = default)
    {
        var collection = await collectionRepo.GetProjectedByIdAsync(collectionId, c => new SyncTarget(
            c.Id,
            c.Title,
            c.Description,
            c.ContentSyncProviderId,
            c.ContentSyncExternalId,
            c.RulesJson,
            c.SmartMediaType,
            c.MirrorList,
            c.LibraryId));

        if (collection == null)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(collection.RulesJson))
            {
                await SyncSmartMembershipAsync(collection, collection.RulesJson);
            }
            else if (!string.IsNullOrEmpty(collection.ContentSyncProviderId) && !string.IsNullOrEmpty(collection.ContentSyncExternalId))
            {
                await SyncProviderMembershipAsync(collection, collection.ContentSyncProviderId, collection.ContentSyncExternalId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync content for collection '{Title}'.", collection.Title);
        }
    }

    private async Task SyncSmartMembershipAsync(SyncTarget collection, string rulesJson)
    {
        var existingMediaIds = await collectionRepo.GetCollectionMediaIdsAsync(collection.Id);
        var ruleMatches = await EvaluateSmartMembership(rulesJson, collection.SmartMediaType, collection.LibraryId);
        var excluded = await collectionRepo.GetExcludedMediaIdsAsync(collection.Id);
        ruleMatches.ExceptWith(excluded);
        await ReconcileMembershipAsync(collection.Id, collection.Title, true, ruleMatches, existingMediaIds);
        await collectionRepo.TouchContentSyncedAtAsync(collection.Id);
    }

    private async Task SyncProviderMembershipAsync(SyncTarget collection, string providerId, string externalId, CancellationToken cancellationToken)
    {
        var existingMediaIds = await collectionRepo.GetCollectionMediaIdsAsync(collection.Id);

        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        if (provider == null)
        {
            logger.LogWarning("Collection Sync Provider '{ProviderId}' not found.", providerId);
            return;
        }

        var collectionIsEmpty = existingMediaIds.Count == 0;

        if (provider is ILibraryCollectionSyncProvider libraryProvider)
        {
            var found = await libraryProvider.FindInLibraryAsync(
                new LibraryCollectionSyncRequest(collection.Id, externalId, collection.LibraryId), cancellationToken);
            await FillBlankDescriptionAsync(provider, collection.Id, collection.Description, externalId, cancellationToken);

            await collectionRepo.TouchContentSyncedAtAsync(collection.Id);

            var themed = found.ToHashSet();
            themed.ExceptWith(await collectionRepo.GetExcludedMediaIdsAsync(collection.Id));
            if (themed.Count == 0)
            {
                if (collectionIsEmpty)
                {
                    await adminNotifications.RaiseAsync(AdminNotificationSeverity.Warning,
                        $"'{collection.Title}' found nothing in your library for its theme",
                        "The AI Theme looks for titles TMDB tags with the theme and checks the titles whose descriptions come closest to it. Name the occasion itself (e.g. \"Halloween\"), and check that AI Settings has an embedding model and the nightly task has described your titles.");
                }
                return;
            }

            await ReconcileMembershipAsync(collection.Id, collection.Title, collection.MirrorList, themed, existingMediaIds);
            return;
        }

        // The provider contract has no token, so the fetch itself runs to
        // completion; checkpoint on either side so a cancel between the fetch
        // and the (possibly second) description call is honored.
        cancellationToken.ThrowIfCancellationRequested();
        var externalItems = await provider.FetchItemsAsync(externalId);
        if (externalItems == null || !externalItems.Any())
        {
            if (collectionIsEmpty)
            {
                await adminNotifications.RaiseAsync(AdminNotificationSeverity.Warning,
                    $"'{collection.Title}' got no titles from the AI",
                    "The AI List works best for a specific franchise or shared universe (e.g. \"Marvel Cinematic Universe\"). A broad genre or mood (e.g. \"kung fu movies\") often returns nothing — build those with a Smart Collection instead.");
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

        await FillBlankDescriptionAsync(provider, collection.Id, collection.Description, externalId, cancellationToken);

        var tmdbIds = externalItems.Select(x => x.TmdbId).OfType<string>().Where(id => id.Length > 0).ToList();
        var imdbIds = externalItems.Select(x => x.ImdbId).OfType<string>().Where(id => id.Length > 0).ToList();

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

        if (matchedIds.Count == 0)
        {
            if (collectionIsEmpty)
            {
                await adminNotifications.RaiseAsync(AdminNotificationSeverity.Warning,
                    $"'{collection.Title}' matched nothing in your library",
                    $"The AI listed {externalItems.Count} title(s) for this collection, but none matched an item in your library. Check the description names the right franchise, or that the titles are in a scanned library.");
            }
            return;
        }

        await ReconcileMembershipAsync(collection.Id, collection.Title, collection.MirrorList, matchedIds, existingMediaIds);
    }

    private async Task FillBlankDescriptionAsync(ICollectionSyncProvider provider, Guid collectionId, string? description, string externalId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(description)) return;

        cancellationToken.ThrowIfCancellationRequested();
        var generatedDescription = await provider.GenerateDescriptionAsync(externalId);
        if (string.IsNullOrWhiteSpace(generatedDescription)) return;

        await collectionRepo.UpdateDescriptionAsync(collectionId, generatedDescription.Trim());
        await notifier.NotifyCollectionUpdatedAsync(collectionId);
    }

    private async Task<HashSet<Guid>> EvaluateSmartMembership(string rulesJson, PlaylistMediaType? smartMediaType, Guid? libraryId)
    {
        var definition = JsonSerializer.Deserialize<SmartPlaylistDefinition>(rulesJson, SmartRuleJsonOptions);
        if (definition?.Root == null)
        {
            return new HashSet<Guid>();
        }

        var access = libraryId.HasValue
            ? new PlaylistAccessFilter { HasAllLibraryAccess = false, AllowedLibraryIds = new List<Guid> { libraryId.Value } }
            : PlaylistAccessFilter.Unrestricted;

        var ids = await smartEvaluator.EvaluateIdsAsync(definition, smartMediaType ?? PlaylistMediaType.Movies, Guid.Empty, access);
        return ids.ToHashSet();
    }

    private async Task ReconcileMembershipAsync(Guid collectionId, string title, bool mirror, HashSet<Guid> desiredIds, HashSet<Guid> existingMediaIds)
    {
        var manuallyAddedIds = await collectionRepo.GetManuallyAddedMediaIdsAsync(collectionId);
        var membershipChanged = false;

        if (mirror)
        {
            var toRemove = existingMediaIds.Where(id => !desiredIds.Contains(id) && !manuallyAddedIds.Contains(id)).ToList();
            if (toRemove.Count > 0)
            {
                await collectionRepo.RemoveItemsFromCollectionAsync(collectionId, toRemove);
                await notifier.NotifyCollectionUpdatedAsync(collectionId);
                logger.LogInformation("Sync removed {Count} item(s) no longer matching from '{Title}'.", toRemove.Count, title);
                membershipChanged = true;
            }
        }

        var itemsToAdd = desiredIds
            .Where(id => !existingMediaIds.Contains(id))
            .Select(id => new CollectionItem
            {
                CollectionId = collectionId,
                MediaItemId = id
            })
            .ToList();

        if (itemsToAdd.Count > 0)
        {
            await collectionRepo.AddItemsToCollectionAsync(itemsToAdd);
            logger.LogInformation("Auto-synced {Count} new items to collection '{Title}'.", itemsToAdd.Count, title);
            await notifier.NotifyCollectionUpdatedAsync(collectionId);
            membershipChanged = true;
        }

        if (membershipChanged)
        {
            taskQueue.QueueReevaluateCollectionOrder(collectionId);
        }
    }

    internal sealed record SyncTarget(
        Guid Id,
        string Title,
        string? Description,
        string? ContentSyncProviderId,
        string? ContentSyncExternalId,
        string? RulesJson,
        PlaylistMediaType? SmartMediaType,
        bool MirrorList,
        Guid? LibraryId);

    private static bool NeedsTitleMatch(CollectionMembershipEntry entry) =>
        string.IsNullOrEmpty(entry.TmdbId)
        && string.IsNullOrEmpty(entry.ImdbId)
        && (!string.IsNullOrWhiteSpace(entry.Title) || !string.IsNullOrWhiteSpace(entry.ShowTitle));
}
