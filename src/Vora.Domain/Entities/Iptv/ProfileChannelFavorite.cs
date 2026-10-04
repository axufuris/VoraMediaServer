using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Iptv;

public class ProfileChannelFavorite
{
    public Guid ProfileId { get; set; }
    public virtual UserProfile? Profile { get; set; }

    public Guid PlaylistId { get; set; }
    public virtual IptvPlaylist? Playlist { get; set; }

    public string ExternalChannelId { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
