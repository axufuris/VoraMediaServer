namespace Vora.Domain.Entities.Media;

public class MediaDedupeIgnoredGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;

    public string Resolution { get; set; } = string.Empty;

    public DateTime IgnoredAt { get; set; } = DateTime.UtcNow;
    public string? IgnoredByProfileId { get; set; }
    public string? Note { get; set; }
}
