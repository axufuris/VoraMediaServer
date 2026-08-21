namespace Vora.Domain.Entities.Media;

public class MediaPart
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string FilePath { get; set; }
    public string? VersionName { get; set; }
    public string? Edition { get; set; }
    public int PartNumber { get; set; } = 1;

    public string? Container { get; set; }
    public string? Resolution { get; set; }
    public long? FileSizeBytes { get; set; }
    public long? OverallBitrate { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime? LastAnalyzedAt { get; set; }

    // Scrub-bar thumbnails are per-part so a different cut (a runtime that differs
    // from a sibling by more than a few seconds) gets its own sprite. Parts that
    // share a cut share one sprite: ThumbnailSourcePartId points at the part that
    // actually owns the generated files (itself, or a same-runtime sibling), and
    // the sprite metadata below is copied from that source so the VTT can be built.
    public Guid? ThumbnailSourcePartId { get; set; }
    public string? VideoThumbnailSpriteVersion { get; set; }
    public int VideoThumbnailSpriteCount { get; set; }
    public int VideoThumbnailIntervalSeconds { get; set; }
    public int VideoThumbnailSpriteColumns { get; set; }
    public int VideoThumbnailWidth { get; set; }
    public int VideoThumbnailHeight { get; set; }

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
