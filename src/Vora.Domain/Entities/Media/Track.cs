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

    // The recording's International Standard Recording Code, read from the file.
    // It identifies one recording exactly, so the explicit and the clean edit of
    // a song carry different codes, which is what lets a provider's explicit flag
    // be matched to this file rather than to whichever edition a title search
    // happens to return.
    public string? Isrc { get; set; }

    // Where ContentRating came from: null when it was read from the file's own
    // tags, otherwise the id of the provider plugin that supplied it. A file tag
    // always replaces a provider's answer, because the tag describes this file
    // and the provider only describes a recording it matched.
    public string? ContentRatingProvider { get; set; }

    // When a provider was last asked about this track's rating, answer or not, so
    // a track no provider knows is not asked about again every night.
    public DateTime? ContentRatingCheckedAt { get; set; }

    public bool HasEmbeddedLyrics { get; set; }
    public string? ExternalLyricsPath { get; set; }
}
