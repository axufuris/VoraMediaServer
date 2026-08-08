using Vora.Domain.Enums;

namespace Vora.Application.Collections.Requests;

public class CreateCollectionRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public CollectionSortOrder DefaultSort { get; set; }
    public string? SortProviderId { get; set; }
    public string? ExternalListId { get; set; }
    public bool AutoSyncChronology { get; set; }
    public string? SortTitle { get; set; }
    public DateTime? VisibleStartDate { get; set; }
    public DateTime? VisibleEndDate { get; set; }
    public bool SystemGenerated { get; set; }
    public string? ContentSyncProviderId { get; set; }
    public string? ContentSyncExternalId { get; set; }
    public int SyncIntervalDays { get; set; } = 1;
    public bool MirrorList { get; set; }
    public Guid? LibraryId { get; set; }
}
