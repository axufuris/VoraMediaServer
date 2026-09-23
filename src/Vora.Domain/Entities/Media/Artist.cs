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

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
}
