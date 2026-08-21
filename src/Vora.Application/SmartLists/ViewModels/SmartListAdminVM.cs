using System.Linq.Expressions;
using Vora.Domain.Entities.SmartLists;

namespace Vora.Application.SmartLists.ViewModels;

public class SmartListAdminVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? LibraryId { get; set; }
    public string FilterRulesJson { get; set; } = string.Empty;
    public int SortBy { get; set; }
    public int MaxItems { get; set; }
    public int DisplayOrder { get; set; }
    public bool ShowOnHomepage { get; set; }
    public bool ShowToFriends { get; set; }
    public bool IsSpotlight { get; set; }
    public Guid? CollectionId { get; set; }

    public static Expression<Func<SmartList, SmartListAdminVM>> Projection => list => new SmartListAdminVM
    {
        Id = list.Id,
        Title = list.Title,
        LibraryId = list.LibraryId,
        FilterRulesJson = list.FilterRulesJson,
        SortBy = (int)list.SortBy,
        MaxItems = list.MaxItems,
        DisplayOrder = list.DisplayOrder,
        ShowOnHomepage = list.ShowOnHomepage,
        ShowToFriends = list.ShowToFriends,
        CollectionId = list.CollectionId,
        IsSpotlight = list.IsSpotlight
    };
}