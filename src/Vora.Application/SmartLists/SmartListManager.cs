using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.Requests;
using Vora.Application.SmartLists.ViewModels;
using Vora.Domain.Entities.SmartLists;

namespace Vora.Application.SmartLists;

public interface ISmartListManager
{
    Task<List<SmartListClientVM>> GetActiveSmartListsAsync(Guid userId, bool isAdmin);
    Task<IEnumerable<LibraryItemVM>> GetSmartListItemsAsync(Guid listId, Guid? profileId, Guid? libraryId, bool hasAllAccess, List<Guid> allowedLibs, bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated);
    Task<List<SmartListAdminVM>> GetAllAdminListsAsync();
    Task<Guid> CreateListAsync(SmartListSaveRequest request);
    Task<bool> UpdateListAsync(Guid id, SmartListSaveRequest request);
    Task<bool> DeleteListAsync(Guid id);
    Task ReorderListsAsync(List<Guid> orderedListIds);
    Task<bool> SetSpotlightAsync(Guid id, bool enabled);
}

public class SmartListManager(
    ISmartListRepository repository,
    IClientNotifier notifier,
    ILogger<SmartListManager> logger) : ISmartListManager
{
    private static readonly JsonSerializerOptions RuleParseOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<List<SmartListClientVM>> GetActiveSmartListsAsync(Guid userId, bool isAdmin) =>
        repository.GetActiveClientListsAsync(isAdmin);

    public Task<List<SmartListAdminVM>> GetAllAdminListsAsync() =>
        repository.GetAllAdminListsAsync();

    public async Task<IEnumerable<LibraryItemVM>> GetSmartListItemsAsync(
        Guid listId,
        Guid? profileId,
        Guid? libraryId,
        bool hasAllAccess,
        List<Guid> allowedLibs,
        bool hasAllRatings,
        List<string> allowedMovieRatings,
        List<string> allowedTvRatings,
        bool blockUnrated)
    {
        var list = await repository.GetListByIdAsync(listId);
        if (list == null)
        {
            return new List<LibraryItemVM>();
        }

        var rules = ParseRules(list.FilterRulesJson);
        var items = await repository.GetSmartListItemsAsync(
            profileId,
            libraryId,
            rules,
            list.SortBy,
            list.MaxItems,
            list.CollectionId,
            hasAllAccess,
            allowedLibs,
            hasAllRatings,
            allowedMovieRatings,
            allowedTvRatings,
            blockUnrated);

        if (profileId.HasValue)
        {
            await repository.AttachLibraryItemUserStatesAsync(items, profileId.Value);
        }

        return items;
    }

    public async Task<Guid> CreateListAsync(SmartListSaveRequest request)
    {
        var list = new SmartList
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            FilterRulesJson = request.FilterRulesJson,
            SortBy = request.SortBy,
            MaxItems = request.MaxItems,
            DisplayOrder = request.DisplayOrder,
            ShowOnHomepage = request.ShowOnHomepage,
            ShowToFriends = request.ShowToFriends,
            IsSpotlight = request.IsSpotlight,
            ActiveStartMonth = request.ActiveStartMonth,
            ActiveStartDay = request.ActiveStartDay,
            ActiveEndMonth = request.ActiveEndMonth,
            ActiveEndDay = request.ActiveEndDay,
            LibraryId = request.LibraryId,
            CollectionId = request.CollectionId
        };

        try
        {
            await repository.CreateListAsync(list);
            await notifier.NotifySmartListsUpdatedAsync();
            return list.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create smart list '{Title}'.", list.Title);
            throw;
        }
    }

    public async Task<bool> UpdateListAsync(Guid id, SmartListSaveRequest request)
    {
        var list = await repository.GetListByIdAsync(id);
        if (list == null)
        {
            return false;
        }

        list.MaxItems = request.MaxItems;
        list.ShowOnHomepage = request.ShowOnHomepage;
        list.ShowToFriends = request.ShowToFriends;
        list.DisplayOrder = request.DisplayOrder;
        list.CollectionId = request.CollectionId;
        // Spotlight is managed exclusively through SetSpotlightAsync (the row
        // toggle), not the edit form, so it isn't clobbered on save.
        list.ActiveStartMonth = request.ActiveStartMonth;
        list.ActiveStartDay = request.ActiveStartDay;
        list.ActiveEndMonth = request.ActiveEndMonth;
        list.ActiveEndDay = request.ActiveEndDay;
        list.LibraryId = request.LibraryId;
        list.Title = request.Title;
        list.FilterRulesJson = request.FilterRulesJson;
        list.SortBy = request.SortBy;

        try
        {
            await repository.UpdateListAsync(list);
            await notifier.NotifySmartListsUpdatedAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update smart list {ListId}.", id);
            throw;
        }
    }

    public async Task<bool> DeleteListAsync(Guid id)
    {
        var list = await repository.GetListByIdAsync(id);
        if (list == null)
        {
            return false;
        }

        try
        {
            await repository.DeleteListAsync(id);
            await notifier.NotifySmartListsUpdatedAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete smart list {ListId}.", id);
            throw;
        }
    }

    public async Task<bool> SetSpotlightAsync(Guid id, bool enabled)
    {
        var list = await repository.GetListByIdAsync(id);
        if (list == null)
        {
            return false;
        }

        await repository.SetSpotlightAsync(id, enabled);
        await notifier.NotifySmartListsUpdatedAsync();
        return true;
    }

    public async Task ReorderListsAsync(List<Guid> orderedListIds)
    {
        try
        {
            await repository.ReorderListsAsync(orderedListIds);
            await notifier.NotifySmartListsUpdatedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reorder smart lists.");
            throw;
        }
    }

    private static SmartListRulesDto? ParseRules(string? filterRulesJson)
    {
        if (string.IsNullOrWhiteSpace(filterRulesJson) || filterRulesJson == "{}")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SmartListRulesDto>(filterRulesJson, RuleParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
