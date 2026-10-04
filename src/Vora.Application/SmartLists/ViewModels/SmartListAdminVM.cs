using System.Linq.Expressions;
using Vora.Domain.Entities.SmartLists;
using Vora.Domain.Enums;

namespace Vora.Application.SmartLists.ViewModels;

public class SmartListAdminVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public SmartListSource Source { get; set; }
    public string? DefaultKey { get; set; }
    public Guid? LibraryId { get; set; }
    public string FilterRulesJson { get; set; } = string.Empty;
    public SmartListSortBy SortBy { get; set; }
    public int MaxItems { get; set; }
    public int DisplayOrder { get; set; }
    public bool ShowOnHomepage { get; set; }
    public bool ShowToFriends { get; set; }
    public int? ActiveStartMonth { get; set; }
    public int? ActiveStartDay { get; set; }
    public int? ActiveEndMonth { get; set; }
    public int? ActiveEndDay { get; set; }
    public Guid? CollectionId { get; set; }

    public static Expression<Func<SmartList, SmartListAdminVM>> Projection => list => new SmartListAdminVM
    {
        Id = list.Id,
        Title = list.Title,
        Source = list.Source,
        DefaultKey = list.DefaultKey,
        LibraryId = list.LibraryId,
        FilterRulesJson = list.FilterRulesJson,
        SortBy = list.SortBy,
        MaxItems = list.MaxItems,
        DisplayOrder = list.DisplayOrder,
        ShowOnHomepage = list.ShowOnHomepage,
        ShowToFriends = list.ShowToFriends,
        ActiveStartMonth = list.ActiveStartMonth,
        ActiveStartDay = list.ActiveStartDay,
        ActiveEndMonth = list.ActiveEndMonth,
        ActiveEndDay = list.ActiveEndDay,
        CollectionId = list.CollectionId
    };
}
