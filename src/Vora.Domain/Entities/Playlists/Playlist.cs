using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Playlists;

public class Playlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public PlaylistMediaType MediaType { get; set; } = PlaylistMediaType.Mixed;

    public virtual ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}
