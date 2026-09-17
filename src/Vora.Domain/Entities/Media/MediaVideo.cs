namespace Vora.Domain.Entities.Media;

public class MediaVideo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string VideoKey { get; set; }
    public required string Name { get; set; }
    public required string Site { get; set; }
    public required string Type { get; set; }
    public bool IsOfficial { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
