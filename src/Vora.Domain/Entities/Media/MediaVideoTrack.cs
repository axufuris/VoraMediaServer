namespace Vora.Domain.Entities.Media;

public class MediaVideoTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? Profile { get; set; }
    public string? HdrType { get; set; }
    public int? BitDepth { get; set; }
    public long? Bitrate { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
    public int? CropTop { get; set; }
    public int? CropRight { get; set; }
    public int? CropBottom { get; set; }
    public int? CropLeft { get; set; }
    public bool IsDefault { get; set; }

    public Guid MediaPartId { get; set; }
    public virtual MediaPart MediaPart { get; set; } = null!;
}
