using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Iptv;

public class IptvChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string ExternalChannelId { get; set; }
    public required string Name { get; set; }
    public required string StreamUrl { get; set; }

    public string? LogoUrl { get; set; }
    public string? GroupTitle { get; set; }
    public string? Resolution { get; set; }
    public string? CountryCode { get; set; }
    public bool IsHiddenByAdmin { get; set; }

    public IptvChannelKind Kind { get; set; }
    public bool KindOverriddenByAdmin { get; set; }

    public Guid PlaylistId { get; set; }
    public virtual IptvPlaylist Playlist { get; set; } = null!;
}
