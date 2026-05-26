using Vora.Domain.Enums;

namespace Vora.Application.SmartLists.Requests;

public class SmartListSaveRequest
{
    public string Title { get; set; } = string.Empty;
    public string FilterRulesJson { get; set; } = "{}";
    public SmartListSortBy SortBy { get; set; } = SmartListSortBy.DateAddedDesc;
    public int MaxItems { get; set; } = 20;
    public int DisplayOrder { get; set; }
    public bool ShowOnHomepage { get; set; } = true;
    public bool ShowToFriends { get; set; } = true;
    public bool IsSpotlight { get; set; }
    public int? ActiveStartMonth { get; set; }
    public int? ActiveStartDay { get; set; }
    public int? ActiveEndMonth { get; set; }
    public int? ActiveEndDay { get; set; }
    public Guid? LibraryId { get; set; }
    public Guid? CollectionId { get; set; }
}
