namespace Vora.Domain.Entities.Media;

public class MediaSubtitleTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Set for a sidecar file sitting next to the video; null for a stream inside
    // the container. StreamIndex is only meaningful for the embedded case — an
    // external track is addressed by its path, not by a position in the file.
    public string? ExternalFilePath { get; set; }

    public bool IsExternal => ExternalFilePath != null;

    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public string? Title { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }

    public Guid MediaPartId { get; set; }
    public virtual MediaPart MediaPart { get; set; } = null!;
}
