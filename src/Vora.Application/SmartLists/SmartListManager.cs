using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Settings;
using Vora.Application.SmartLists.Dtos;
using Vora.Application.SmartLists.Requests;
using Vora.Application.SmartLists.ViewModels;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.SmartLists;

public interface ISmartListManager
{
    Task<List<SmartListClientVM>> GetActiveSmartListsAsync(bool isAdmin);
    Task<IEnumerable<LibraryItemVM>> GetSmartListItemsAsync(Guid listId, SmartListViewer viewer);
    Task<List<SmartListEntryVM>> GetSmartListEntriesAsync(Guid listId, SmartListViewer viewer);
    Task<List<SmartListAdminVM>> GetAllAdminListsAsync();
    Task<List<SmartListDefaultVM>> GetDefaultListsAsync();
    Task<int> RestoreDefaultListsAsync();
    Task<Guid> CreateListAsync(SmartListSaveRequest request);
    Task<bool> UpdateListAsync(Guid id, SmartListSaveRequest request);
    Task<bool> DeleteListAsync(Guid id);
    Task ReorderListsAsync(List<Guid> orderedListIds);
}

public class SmartListManager(
    ISmartListRepository repository,
    ISmartListSourceResolver sourceResolver,
    ISystemSettingsRepository settingsRepository,
    IClientNotifier notifier,
    ILogger<SmartListManager> logger) : ISmartListManager
{
    public const int MinListItems = 1;
    public const int MaxListItems = 100;

    private static readonly JsonSerializerOptions RuleParseOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<SmartListClientVM>> GetActiveSmartListsAsync(bool isAdmin)
    {
        var lists = await repository.GetActiveClientListsAsync(isAdmin);
        var settings = await settingsRepository.GetSettingsAsync();
        return lists.Where(l => IsSourceEnabled(l.Source, settings)).ToList();
    }

    public Task<List<SmartListAdminVM>> GetAllAdminListsAsync() =>
        repository.GetAllAdminListsAsync();

    public async Task<IEnumerable<LibraryItemVM>> GetSmartListItemsAsync(Guid listId, SmartListViewer viewer)
    {
        var list = await repository.GetListByIdAsync(listId);
        if (list == null || list.Source != SmartListSource.Library)
        {
            return new List<LibraryItemVM>();
        }

        return await GetLibraryItemsAsync(list, viewer);
    }

    public async Task<List<SmartListEntryVM>> GetSmartListEntriesAsync(Guid listId, SmartListViewer viewer)
    {
        var list = await repository.GetListByIdAsync(listId);
        if (list == null)
        {
            return [];
        }

        if (list.Source == SmartListSource.Library)
        {
            var items = await GetLibraryItemsAsync(list, viewer);
            return items.Select(item => new SmartListEntryVM
            {
                Kind = SmartListEntryKind.Media,
                Id = item.Id,
                Title = item.Title,
                Subtitle = item.Artist ?? item.TvShowTitle,
                ImageUrl = item.PosterUrl,
                Media = item
            }).ToList();
        }

        var settings = await settingsRepository.GetSettingsAsync();
        if (!IsSourceEnabled(list.Source, settings))
        {
            return [];
        }

        return await sourceResolver.ResolveAsync(list, ParseRules(list.FilterRulesJson), viewer);
    }

    public async Task<List<SmartListDefaultVM>> GetDefaultListsAsync()
    {
        var present = await repository.GetDefaultKeysAsync();
        return SmartListDefaults.All.Select(d => new SmartListDefaultVM
        {
            Key = d.Key,
            Title = d.Title,
            Source = d.Source,
            IsPresent = present.Contains(d.Key)
        }).ToList();
    }

    public async Task<int> RestoreDefaultListsAsync()
    {
        var present = await repository.GetDefaultKeysAsync();
        var missing = SmartListDefaults.All.Where(d => !present.Contains(d.Key)).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var nextOrder = await repository.GetMaxDisplayOrderAsync() + 1;
        foreach (var definition in missing)
        {
            var list = definition.ToEntity();
            list.DisplayOrder = nextOrder++;
            await repository.CreateListAsync(list);
        }

        await notifier.NotifySmartListsUpdatedAsync();
        return missing.Count;
    }

    public async Task<Guid> CreateListAsync(SmartListSaveRequest request)
    {
        var list = new SmartList { Id = Guid.NewGuid() };
        Apply(list, request);

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

        Apply(list, request);

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

    public static bool IsSourceEnabled(SmartListSource source, ServerSetting settings) => source switch
    {
        SmartListSource.FavoriteChannels => settings.EnableLiveTv,
        SmartListSource.FavoriteStations => settings.EnableInternetRadio,
        SmartListSource.NewPodcastEpisodes => settings.EnablePodcasts,
        SmartListSource.RecentRecordings => settings.EnableLiveTv && settings.EnableDvr,
        _ => true
    };

    private async Task<List<LibraryItemVM>> GetLibraryItemsAsync(SmartList list, SmartListViewer viewer)
    {
        var items = await repository.GetSmartListItemsAsync(
            viewer.ProfileId,
            list.LibraryId,
            ParseRules(list.FilterRulesJson),
            list.SortBy,
            list.MaxItems,
            list.CollectionId,
            viewer.HasAllLibraryAccess,
            viewer.AllowedLibraryIds,
            viewer.AllowedMovieRatings,
            viewer.AllowedTvRatings,
            viewer.BlockUnrated);

        if (viewer.ProfileId is { } profileId)
        {
            await repository.AttachLibraryItemUserStatesAsync(items, profileId);
        }

        return items;
    }

    private static void Apply(SmartList list, SmartListSaveRequest request)
    {
        var isLibrary = request.Source == SmartListSource.Library;

        list.Title = request.Title.Trim();
        list.Source = request.Source;
        list.FilterRulesJson = string.IsNullOrWhiteSpace(request.FilterRulesJson) ? "{}" : request.FilterRulesJson;
        list.SortBy = request.SortBy;
        list.MaxItems = Math.Clamp(request.MaxItems, MinListItems, MaxListItems);
        list.DisplayOrder = request.DisplayOrder;
        list.ShowOnHomepage = request.ShowOnHomepage;
        list.ShowToFriends = request.ShowToFriends;
        var hasWindow = IsCalendarDay(request.ActiveStartMonth, request.ActiveStartDay) && IsCalendarDay(request.ActiveEndMonth, request.ActiveEndDay);
        list.ActiveStartMonth = hasWindow ? request.ActiveStartMonth : null;
        list.ActiveStartDay = hasWindow ? request.ActiveStartDay : null;
        list.ActiveEndMonth = hasWindow ? request.ActiveEndMonth : null;
        list.ActiveEndDay = hasWindow ? request.ActiveEndDay : null;
        list.CollectionId = isLibrary ? request.CollectionId : null;
        list.LibraryId = isLibrary || request.Source == SmartListSource.RecentlyAddedMusic ? request.LibraryId : null;
    }

    private static bool IsCalendarDay(int? month, int? day) =>
        month is >= 1 and <= 12 && day is >= 1 && day <= DateTime.DaysInMonth(2000, month.Value);

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
