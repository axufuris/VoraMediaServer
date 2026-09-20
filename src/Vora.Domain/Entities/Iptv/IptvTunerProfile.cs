namespace Vora.Domain.Entities.Iptv;

public class IptvTunerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int MaxConcurrentStreams { get; set; }

    public Guid PlaylistId { get; set; }
    public virtual IptvPlaylist Playlist { get; set; } = null!;
}
