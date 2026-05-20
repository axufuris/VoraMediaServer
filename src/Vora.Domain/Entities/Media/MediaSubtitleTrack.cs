namespace Vora.Domain.Entities.Media;

public class MediaSubtitleTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }

    public Guid MediaPartId { get; set; }
    public virtual MediaPart MediaPart { get; set; } = null!;
}
