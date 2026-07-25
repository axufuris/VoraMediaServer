namespace Vora.Domain.Entities.Media;

public class MediaPart
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string FilePath { get; set; }
    public string? VersionName { get; set; }
    public int PartNumber { get; set; } = 1;

    public string? Container { get; set; }
    public string? Resolution { get; set; }
    public long? FileSizeBytes { get; set; }
    public long? OverallBitrate { get; set; }
    public TimeSpan? Duration { get; set; }

    // A part belongs to exactly one owner: either a MediaItem (the item's own
    // file) or a MediaExtra (a trailer/featurette file). Both FKs are nullable;
    // exactly one is set.
    public Guid? MediaItemId { get; set; }
    public virtual MediaItem? MediaItem { get; set; }

    public Guid? MediaExtraId { get; set; }
    public virtual MediaExtra? MediaExtra { get; set; }

    public virtual ICollection<MediaVideoTrack> VideoTracks { get; set; } = new List<MediaVideoTrack>();
    public virtual ICollection<MediaAudioTrack> AudioTracks { get; set; } = new List<MediaAudioTrack>();
    public virtual ICollection<MediaSubtitleTrack> SubtitleTracks { get; set; } = new List<MediaSubtitleTrack>();
}
