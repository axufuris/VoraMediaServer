using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Playlists;

public class PlaylistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public Guid PlaylistId { get; set; }
    public virtual Playlist Playlist { get; set; } = null!;

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
