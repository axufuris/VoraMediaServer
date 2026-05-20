using System.Linq.Expressions;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Collections.ViewModels;

public class CollectionDetailsVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public string DefaultSortName { get; set; } = string.Empty;
    public CollectionSortOrder DefaultSort { get; set; }
    public bool IsMixedCollection { get; set; }
    public int ItemCount { get; set; }
    public Guid? LibraryId { get; set; }
    public string? SortProviderId { get; set; }
    public string? ExternalListId { get; set; }
    public bool AutoSyncChronology { get; set; }
    public string? SortTitle { get; set; }
    public DateTime? VisibleStartDate { get; set; }
    public DateTime? VisibleEndDate { get; set; }
    public bool SystemGenerated { get; set; }
    public string? ContentSyncProviderId { get; set; }
    public string? ContentSyncExternalId { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemcount { get; set; }
    public List<string> LockedFields { get; set; } = new();
    public List<CollectionDetailsLibraryItemVM> Items { get; set; } = new();

    public static Expression<Func<Collection, CollectionDetailsVM>> Projection =>
        c => new CollectionDetailsVM
        {
            Id = c.Id,
            Title = c.Title,
            Description = c.Description,
            PosterUrl = c.PosterUrl,
            BackdropUrl = c.BackdropUrl,
            DefaultSortName = c.DefaultSort.ToString(),
            DefaultSort = c.DefaultSort,
            IsMixedCollection = c.LibraryId == null,
            ItemCount = c.Items.Count,
            LibraryId = c.LibraryId,
            SortProviderId = c.SortProviderId,
            ExternalListId = c.ExternalListId,
            AutoSyncChronology = c.AutoSyncChronology,
            SortTitle = c.SortTitle,
            VisibleStartDate = c.VisibleStartDate,
            VisibleEndDate = c.VisibleEndDate,
            SystemGenerated = c.SystemGenerated,
            LockedFields = c.LockedFields,
            ContentSyncProviderId = c.ContentSyncProviderId,
            ContentSyncExternalId = c.ContentSyncExternalId,
            IsPlayed = false,
            UnplayedItemcount = null,
            Items = c.Items.Select(item => new CollectionDetailsLibraryItemVM
            {
                Id = item.Id,
                Title = item.Title,
                SortTitle = item.SortTitle,
                Type = item is Movie ? "Movie"
                    : item is TvShow ? "TvShow"
                    : item is Season ? "Season"
                    : item is Episode ? "Episode"
                    : "Unknown",
                PosterUrl = item.PosterUrl,
                ReleaseDate = item.ReleaseDate,
                IsPlayed = false,
                UnplayedItemCount = null
            }).ToList()
        };
}
