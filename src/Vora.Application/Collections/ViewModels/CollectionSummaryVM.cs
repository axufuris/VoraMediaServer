using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Vora.Domain.Entities.Library;

namespace Vora.Application.Collections.ViewModels;

public class CollectionSummaryVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public int ItemCount { get; set; }
    public string? SortTitle { get; set; }
    public DateTime? VisibleStartDate { get; set; }
    public DateTime? VisibleEndDate { get; set; }
    public bool VisibleEveryYear { get; set; }
    public bool SystemGenerated { get; set; }
    public CollectionHiddenReason? HiddenReason { get; set; }

    // Read to work out HiddenReason; viewers get only the reason.
    [JsonIgnore]
    public bool Hidden { get; set; }

    // A library's list shows every item of a global collection in ItemCount
    // (what opening it shows), but whether the collection appears there at
    // all, and the minimum-size rule, go by the items in that library.
    [JsonIgnore]
    public int LibraryItemCount { get; set; }

    public static Expression<Func<Collection, CollectionSummaryVM>> StandardProjection =>
        c => new CollectionSummaryVM
        {
            Id = c.Id,
            Title = c.Title,
            PosterUrl = c.PosterUrl,
            ItemCount = c.Items.Count,
            SortTitle = c.SortTitle,
            VisibleStartDate = c.VisibleStartDate,
            VisibleEndDate = c.VisibleEndDate,
            VisibleEveryYear = c.VisibleEveryYear,
            SystemGenerated = c.SystemGenerated,
            Hidden = c.Hidden
        };

    public static Expression<Func<Collection, CollectionSummaryVM>> LibraryProjection(Guid libraryId) =>
        c => new CollectionSummaryVM
        {
            Id = c.Id,
            Title = c.Title,
            PosterUrl = c.PosterUrl,
            SortTitle = c.SortTitle,
            VisibleStartDate = c.VisibleStartDate,
            VisibleEndDate = c.VisibleEndDate,
            VisibleEveryYear = c.VisibleEveryYear,
            SystemGenerated = c.SystemGenerated,
            Hidden = c.Hidden,
            ItemCount = c.Items.Count,
            LibraryItemCount = c.Items.Count(i => i.LibraryId == libraryId)
        };
}
