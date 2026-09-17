namespace Vora.Domain.Entities.Discovery;

public class UserWatchlistItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    // An entry is keyed by its external provider identity when it has one, so a
    // title bookmarked from Discovery and the same title once it lands in the
    // library stay a single row. MediaItemId is set whenever a local copy is
    // known, and is the only key for library items with no external match
    // (home videos, unmatched files).
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public Guid? MediaItemId { get; set; }

    public DateTime? ExpectedReleaseDate { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public Guid ProfileId { get; set; }
}
