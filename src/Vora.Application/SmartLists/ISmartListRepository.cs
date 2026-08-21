using Vora.Application.Libraries.ViewModels;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.ViewModels;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.SmartLists;

public interface ISmartListRepository
{
    Task<List<LibraryItemVM>> GetSmartListItemsAsync(
        Guid? profileId,
        Guid? libraryId,
        SmartListRulesDto? rules,
        SmartListSortBy sortBy,
        int maxItems,
        Guid? collectionId = null,
        bool hasAllAccess = true,
        List<Guid>? allowedLibs = null,
        bool hasAllRatings = true,
        List<string>? allowedMovieRatings = null,
        List<string>? allowedTvRatings = null,
        bool blockUnrated = false);

    Task<List<SmartListClientVM>> GetActiveClientListsAsync(bool isAdmin);
    Task<List<SmartListAdminVM>> GetAllAdminListsAsync();
    Task<SmartList?> GetListByIdAsync(Guid id);
    Task CreateListAsync(SmartList list);
    Task UpdateListAsync(SmartList list);
    Task<bool> DeleteListAsync(Guid id);
    Task ReorderListsAsync(List<Guid> orderedListIds);
    Task SetSpotlightAsync(Guid id, bool enabled);
    Task AttachLibraryItemUserStatesAsync(IEnumerable<LibraryItemVM> items, Guid profileId);
}
