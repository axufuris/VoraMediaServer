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

    // How popular this is in the WORLD, from Last.fm's aggregate over its own
    // users. Named Global so it cannot be mistaken for this server's plays, which
    // live in TrackPlayHistory and drive the Popular section - two different
    // questions that are easy to conflate and wrong to mix.
    // Filled only for tracks among an artist's top tracks on Last.fm. The rest
    // stay null and sort last, which is an honest answer rather than a gap.
    public long? GlobalListeners { get; set; }
    public long? GlobalPlays { get; set; }

    public bool HasEmbeddedLyrics { get; set; }
    public string? ExternalLyricsPath { get; set; }
}
