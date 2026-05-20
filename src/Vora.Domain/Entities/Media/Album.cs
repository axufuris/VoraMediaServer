using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public class Album : LockableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? SortTitle { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? DiscArtUrl { get; set; }

    public string? AlbumArtist { get; set; }
    public bool IsCompilation { get; set; }

    public Guid ArtistId { get; set; }
    public virtual Artist Artist { get; set; } = null!;

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Track> Tracks { get; set; } = new List<Track>();
}
