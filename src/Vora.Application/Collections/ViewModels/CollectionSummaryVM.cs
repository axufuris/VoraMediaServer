using System.Linq.Expressions;
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
    public bool SystemGenerated { get; set; }

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
            SystemGenerated = c.SystemGenerated
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
            SystemGenerated = c.SystemGenerated,
            ItemCount = c.Items.Count(i => i.LibraryId == libraryId)
        };
}
