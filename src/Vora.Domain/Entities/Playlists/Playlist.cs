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

    // A cover the owner uploaded. Null means the clients build one from the
    // artwork of the playlist's first items.
    public string? ImageUrl { get; set; }

    // Shared playlists are visible, read-only, to every profile on the server.
    // Anyone can play one or save a copy they own; only the owner can change it.
    // A copy starts unshared, so saving someone's playlist does not put a second
    // copy of it in everyone's Shared tab.
    public bool IsShared { get; set; }

    // When it was last shared, so the Shared tab can list the newest first.
    // Cleared on unsharing rather than kept, because a playlist that was shared
    // last year and is shared again today is new to everyone looking at it.
    public DateTime? SharedAt { get; set; }

    public virtual ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}
