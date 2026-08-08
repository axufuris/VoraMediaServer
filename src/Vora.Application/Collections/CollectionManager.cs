using Vora.Application.Analysis;
using Vora.Application.Collections.Dtos;
using Vora.Application.Collections.Requests;
using Vora.Application.Collections.ViewModels;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Library;
using Vora.Domain.Enums;

namespace Vora.Application.Collections;

public interface ICollectionManager
{
    Task<List<CollectionScheduleDto>> GetContentSyncCollectionsAsync();
    Task<List<CollectionScheduleDto>> GetAutoSyncCollectionsAsync();
    Task<CollectionDetailsVM?> GetCollectionDetailsAsync(Guid id, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, CollectionSortOrder? sortOverride = null);
    Task<Guid> CreateCollectionAsync(CreateCollectionRequest request);
    Task AddMediaToCollectionAsync(Guid collectionId, Guid mediaItemId);
    Task<IEnumerable<CollectionSummaryVM>> GetLibraryCollectionsAsync(Guid libraryId, bool hasAllAccess, List<Guid> allowedLibs);
    Task<IEnumerable<CollectionSummaryVM>> GetAllCollectionsAsync(bool hasAllAccess, List<Guid> allowedLibs);
    Task<IEnumerable<CollectionSummaryVM>> GetGlobalCollectionsAsync(bool hasAllAccess, List<Guid> allowedLibs);
    Task UpdateCollectionAsync(Guid id, UpdateCollectionRequest request);
    Task RemoveMediaFromCollectionAsync(Guid collectionId, Guid mediaItemId);
    Task ReorderCollectionItemsAsync(Guid collectionId, List<Guid> orderedMediaItemIds);
    Task DeleteCollectionAsync(Guid id);
}

public class CollectionManager : ICollectionManager
{
    private readonly ICollectionRepository _repository;
    private readonly ITaskQueueManager _taskQueueManager;
    private readonly IClientNotifier _notifier;

    public CollectionManager(ICollectionRepository repository, ITaskQueueManager taskQueueManager, IClientNotifier notifier)
    {
        _repository = repository;
        _taskQueueManager = taskQueueManager;
        _notifier = notifier;
    }

    public async Task<List<CollectionScheduleDto>> GetContentSyncCollectionsAsync()
    {
        return await _repository.GetContentSyncCollectionsAsync();
    }

    public async Task<List<CollectionScheduleDto>> GetAutoSyncCollectionsAsync()
    {
        return await _repository.GetAutoSyncCollectionsAsync();
    }

    public async Task<CollectionDetailsVM?> GetCollectionDetailsAsync(Guid id, Guid? profileId, bool hasAllAccess, List<Guid> allowedLibs, CollectionSortOrder? sortOverride = null)
    {
        var collection = await _repository.GetProjectedByIdAsync(id, CollectionDetailsVM.Projection, hasAllAccess, allowedLibs);
        if (collection == null) return null;

        var effectiveSort = sortOverride ?? collection.DefaultSort;

        Dictionary<Guid, decimal>? sortOrders = null;
        if (effectiveSort == CollectionSortOrder.Chronological)
        {
            sortOrders = await _repository.GetCollectionItemSortOrdersAsync(id);
        }

        collection.Items = effectiveSort switch
        {
            CollectionSortOrder.Chronological => collection.Items
                .OrderBy(i => sortOrders != null && sortOrders.TryGetValue(i.Id, out var order) ? order : decimal.MaxValue)
                .ToList(),

            CollectionSortOrder.ReleaseDateDesc => collection.Items.OrderByDescending(i => i.ReleaseDate).ToList(),
            CollectionSortOrder.ReleaseDateAsc => collection.Items.OrderBy(i => i.ReleaseDate).ToList(),
            CollectionSortOrder.DateAddedDesc => collection.Items.OrderByDescending(i => i.AddedAt).ToList(),
            CollectionSortOrder.Alphabetical => collection.Items.OrderBy(i => i.SortTitle ?? i.Title).ToList(),
            _ => collection.Items
        };

        if (profileId.HasValue)
        {
            await _repository.AttachCollectionItemUserStatesAsync(collection.Items, profileId.Value);
        }

        return collection;
    }

    public async Task<Guid> CreateCollectionAsync(CreateCollectionRequest request)
    {
        var collection = new Collection
        {
            Title = request.Title,
            Description = request.Description,
            PosterUrl = request.PosterUrl,
            BackdropUrl = request.BackdropUrl,
            DefaultSort = request.DefaultSort,
            SortProviderId = request.SortProviderId,
            ExternalListId = request.ExternalListId,
            AutoSyncChronology = request.AutoSyncChronology,
            SortTitle = request.SortTitle,
            VisibleStartDate = request.VisibleStartDate.HasValue ? DateTime.SpecifyKind(request.VisibleStartDate.Value, DateTimeKind.Utc) : null,
            VisibleEndDate = request.VisibleEndDate.HasValue ? DateTime.SpecifyKind(request.VisibleEndDate.Value, DateTimeKind.Utc) : null,
            SystemGenerated = request.SystemGenerated,
            LibraryId = request.LibraryId,
            ContentSyncProviderId = request.ContentSyncProviderId,
            ContentSyncExternalId = request.ContentSyncExternalId,
            SyncIntervalDays = Math.Max(1, request.SyncIntervalDays),
            MirrorList = request.MirrorList
        };

        var id = await _repository.CreateCollectionAsync(collection);

        TriggerCollectionSyncTasks(id, request.Title, request.ContentSyncProviderId, request.SortProviderId);

        if (request.LibraryId.HasValue)
        {
            await _notifier.NotifyLibraryUpdatedAsync(request.LibraryId.Value);
        }

        return id;
    }

    public async Task UpdateCollectionAsync(Guid id, UpdateCollectionRequest request)
    {
        var collection = await _repository.GetForUpdateAsync(id);
        if (collection == null) throw new InvalidOperationException("Collection not found");

        collection.Title = request.Title;
        collection.Description = request.Description;
        collection.PosterUrl = request.PosterUrl;
        collection.BackdropUrl = request.BackdropUrl;
        collection.DefaultSort = request.DefaultSort;
        collection.LockedFields = request.LockedFields;
        collection.SortProviderId = request.SortProviderId;
        collection.ExternalListId = request.ExternalListId;
        collection.AutoSyncChronology = request.AutoSyncChronology;
        collection.SortTitle = request.SortTitle;
        collection.ContentSyncExternalId = request.ContentSyncExternalId;
        collection.ContentSyncProviderId = request.ContentSyncProviderId;
        collection.SyncIntervalDays = Math.Max(1, request.SyncIntervalDays);
        collection.MirrorList = request.MirrorList;
        collection.VisibleStartDate = request.VisibleStartDate.HasValue ? DateTime.SpecifyKind(request.VisibleStartDate.Value, DateTimeKind.Utc) : null;
        collection.VisibleEndDate = request.VisibleEndDate.HasValue ? DateTime.SpecifyKind(request.VisibleEndDate.Value, DateTimeKind.Utc) : null;

        if (request.MakeGlobal)
        {
            collection.LibraryId = null;
        }

        await _repository.UpdateCollectionAsync(collection);

        TriggerCollectionSyncTasks(id, request.Title, request.ContentSyncProviderId, request.SortProviderId);

        await _notifier.NotifyCollectionUpdatedAsync(id);
    }

    private void TriggerCollectionSyncTasks(Guid collectionId, string title, string? contentProvider, string? sortProvider)
    {
        bool hasContentSync = !string.IsNullOrEmpty(contentProvider);
        bool hasChronologySort = !string.IsNullOrEmpty(sortProvider);

        if (hasContentSync || hasChronologySort)
        {
            _taskQueueManager.QueueFullCollectionSync(collectionId, title, hasContentSync, hasChronologySort);
        }
    }

    public async Task AddMediaToCollectionAsync(Guid collectionId, Guid mediaItemId)
    {
        await _repository.AddItemToCollectionAsync(collectionId, mediaItemId);

        await _notifier.NotifyCollectionUpdatedAsync(collectionId);

        _taskQueueManager.QueueReevaluateCollectionOrder(collectionId);
    }

    public async Task<IEnumerable<CollectionSummaryVM>> GetLibraryCollectionsAsync(Guid libraryId, bool hasAllAccess, List<Guid> allowedLibs)
    {
        var minSize = await _repository.GetLibraryMinimumCollectionSizeAsync(libraryId);
        if (minSize < 1) minSize = 1;

        var collections = await _repository.GetAllProjectedAsync(CollectionSummaryVM.LibraryProjection(libraryId), libraryId, false, hasAllAccess, allowedLibs);
        return collections.Where(c => c.ItemCount >= minSize).OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<CollectionSummaryVM>> GetAllCollectionsAsync(bool hasAllAccess, List<Guid> allowedLibs)
    {
        var collections = await _repository.GetAllProjectedAsync(CollectionSummaryVM.StandardProjection, null, false, hasAllAccess, allowedLibs);
        return collections.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<CollectionSummaryVM>> GetGlobalCollectionsAsync(bool hasAllAccess, List<Guid> allowedLibs)
    {
        var collections = await _repository.GetAllProjectedAsync(CollectionSummaryVM.StandardProjection, null, true, hasAllAccess, allowedLibs);
        return collections.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);
    }

    public async Task RemoveMediaFromCollectionAsync(Guid collectionId, Guid mediaItemId)
    {
        await _repository.RemoveItemFromCollectionAsync(collectionId, mediaItemId);

        await _notifier.NotifyCollectionUpdatedAsync(collectionId);
    }

    public async Task ReorderCollectionItemsAsync(Guid collectionId, List<Guid> orderedMediaItemIds)
    {
        await _repository.UpdateCollectionItemOrdersAsync(collectionId, orderedMediaItemIds);

        await _notifier.NotifyCollectionUpdatedAsync(collectionId);
    }

    public async Task DeleteCollectionAsync(Guid id)
    {
        var collection = await _repository.GetForUpdateAsync(id);
        if (collection == null) return;

        if (collection.SystemGenerated) throw new InvalidOperationException("System-generated collections cannot be deleted.");

        var libraryId = collection.LibraryId;

        await _repository.DeleteCollectionAsync(id);

        if (libraryId.HasValue)
        {
            await _notifier.NotifyLibraryUpdatedAsync(libraryId.Value);
        }
    }
}