using System.Linq.Expressions;
using Vora.Application.Collections.Dtos;
using Vora.Application.Collections.ViewModels;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Collections;

public interface ICollectionRepository
{
    Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<Collection, T>> projection, bool hasAllAccess = true, List<Guid>? allowedLibs = null);
    Task<IEnumerable<T>> GetAllProjectedAsync<T>(Expression<Func<Collection, T>> projection, Guid? libraryId = null, bool globalOnly = false, bool hasAllAccess = true, List<Guid>? allowedLibs = null);

    Task<List<CollectionScheduleDto>> GetContentSyncCollectionsAsync();
    Task<List<CollectionScheduleDto>> GetAutoSyncCollectionsAsync();

    Task<IEnumerable<CollectionArtwork>> GetCollectionArtworkAsync(Guid collectionId);
    Task<CollectionArtwork?> GetCollectionArtworkByIdAsync(Guid id);
    Task<HashSet<Guid>> GetCollectionMediaIdsAsync(Guid collectionId);
    Task<decimal> GetMaxSortOrderAsync(Guid collectionId);
    Task<List<CollectionItem>> GetCollectionItemsWithMediaAsync(Guid collectionId);
    Task<Dictionary<Guid, decimal>> GetCollectionItemSortOrdersAsync(Guid collectionId);
    Task<int> GetLibraryMinimumCollectionSizeAsync(Guid libraryId);
    Task<Dictionary<Guid, int>> GetAllLibraryMinimumSizesAsync();

    Task<Collection?> GetForUpdateAsync(Guid id);
    Task<Collection?> GetCollectionByTmdbIdAsync(int tmdbId, Guid libraryId);

    Task<Guid> CreateCollectionAsync(Collection collection);
    Task AddCollectionAsync(Collection collection);
    Task UpdateCollectionAsync(Collection collection);
    Task UpdateCollectionItemsAsync(IEnumerable<CollectionItem> items);
    Task UpdateCollectionItemOrdersAsync(Guid collectionId, List<Guid> orderedMediaItemIds);
    Task AddItemsToCollectionAsync(List<CollectionItem> items);
    Task AddItemToCollectionAsync(Guid collectionId, Guid mediaItemId);
    Task RemoveItemFromCollectionAsync(Guid collectionId, Guid mediaItemId);
    Task DeleteCollectionAsync(Guid id);

    Task AddCollectionArtworkAsync(CollectionArtwork artwork);
    Task DeleteCollectionArtworkAsync(Guid id);
    Task ReplaceProviderArtworkAsync(Guid collectionId, IEnumerable<CollectionArtwork> newArtwork);

    Task AttachCollectionItemUserStatesAsync(IEnumerable<CollectionDetailsLibraryItemVM> items, Guid profileId);
}
