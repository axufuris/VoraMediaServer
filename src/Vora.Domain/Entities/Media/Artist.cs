using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;

namespace Vora.Domain.Entities.Media;

public class Artist : LockableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? SortName { get; set; }
    public string? Biography { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ClearLogoUrl { get; set; }

    public Guid LibraryId { get; set; }
    public virtual MediaLibrary Library { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
}
