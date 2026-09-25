using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public class Album : LockableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? SortTitle { get; set; }

    // The MusicBrainz RELEASE-GROUP id, which is what both Fanart.tv and the
    // Cover Art Archive key album artwork by. Kept for the same reason as the
    // artist's: resolving it is a rate-limited search.
    public string? MusicBrainzId { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? DiscArtUrl { get; set; }

    public string? AlbumArtist { get; set; }
    public bool IsCompilation { get; set; }

    public decimal? ServerAdminRating { get; set; }

    // How popular this is in the WORLD, from Last.fm's aggregate over its own
    // users. Named Global so it cannot be mistaken for this server's plays, which
    // live in TrackPlayHistory and drive the Popular section - two different
    // questions that are easy to conflate and wrong to mix.
    // Plays only: Last.fm's artist.getTopAlbums returns a playcount per album and
    // no listener count, so a listeners column here would never be filled.
    public long? GlobalPlays { get; set; }

    // When the artwork providers were last asked about this, whether or not they
    // had anything. Some artwork is missing for good - no provider has album
    // backgrounds, and most artists have no banner or logo - so without this
    // every scan asked again about nearly the whole library.
    public DateTime? ArtworkCheckedAt { get; set; }

    public Guid ArtistId { get; set; }
    public virtual Artist Artist { get; set; } = null!;

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Track> Tracks { get; set; } = new List<Track>();
}
