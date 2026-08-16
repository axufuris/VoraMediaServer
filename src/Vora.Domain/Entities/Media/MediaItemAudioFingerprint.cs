namespace Vora.Domain.Entities.Media;

public class MediaItemAudioFingerprint
{
    public Guid Id { get; set; }

    public byte[]? HeadFingerprint { get; set; }
    public double HeadPointDurationSeconds { get; set; }

    public string FileIdentity { get; set; } = "";
    public DateTime AnalyzedAt { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
