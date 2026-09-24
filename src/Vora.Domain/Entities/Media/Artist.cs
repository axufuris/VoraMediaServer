using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public class Artist : LockableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? SortName { get; set; }

    // Fanart.tv's music API is keyed by MusicBrainz id, so every artwork lookup
    // needed one. Resolving it meant a MusicBrainz search per lookup, against an
    // API that allows about a request a second — so the id is kept once it has
    // been found and MusicBrainz is asked only for artists that have never
    // resolved.
    public string? MusicBrainzId { get; set; }
    public string? Biography { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ClearLogoUrl { get; set; }

    public decimal? ServerAdminRating { get; set; }

    // How popular this is in the WORLD, from Last.fm's aggregate over its own
    // users. Named Global so it cannot be mistaken for this server's plays, which
    // live in TrackPlayHistory and drive the Popular section - two different
    // questions that are easy to conflate and wrong to mix.
    public long? GlobalListeners { get; set; }
    public long? GlobalPlays { get; set; }

    // Set on every refresh attempt, including one where Last.fm knew nothing
    // about the artist. Otherwise an unknown artist would look never-refreshed
    // and be asked about again on every run, which is the call volume this
    // design exists to avoid. One stamp covers the artist's albums and tracks
    // too, because a single refresh fetches all three.
    public DateTime? PopularityRefreshedAt { get; set; }

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
}
