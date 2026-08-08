using Vora.Domain.Enums;

namespace Vora.Application.Collections.Requests;

public class UpdateCollectionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public CollectionSortOrder DefaultSort { get; set; }
    public List<string> LockedFields { get; set; } = new();
    public bool MakeGlobal { get; set; }
    public string? SortProviderId { get; set; }
    public string? ExternalListId { get; set; }
    public bool AutoSyncChronology { get; set; }
    public string? SortTitle { get; set; }
    public string? ContentSyncProviderId { get; set; }
    public string? ContentSyncExternalId { get; set; }
    public int SyncIntervalDays { get; set; } = 1;
    public bool MirrorList { get; set; }
    public DateTime? VisibleStartDate { get; set; }
    public DateTime? VisibleEndDate { get; set; }
}
