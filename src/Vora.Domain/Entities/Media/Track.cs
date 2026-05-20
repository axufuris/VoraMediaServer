namespace Vora.Domain.Entities.Media;

public class Track : MediaItem
{
    public Guid? AlbumId { get; set; }
    public virtual Album? Album { get; set; }

    public string? Artist { get; set; }

    public int TrackNumber { get; set; }
    public int? DiscNumber { get; set; }

    public string? AudioCodec { get; set; }
    public int? SampleRate { get; set; }
    public int? Bitrate { get; set; }
    public int? DurationSeconds { get; set; }

    public bool HasEmbeddedLyrics { get; set; }
    public string? ExternalLyricsPath { get; set; }
}
