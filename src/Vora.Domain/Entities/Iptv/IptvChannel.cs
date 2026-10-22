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

    // null = never checked; true/false = last health-check result. Separate from
    // IsHiddenByAdmin so an auto-hide never collides with a manual hide and can
    // self-heal on the next check.
    public bool? IsHealthy { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }

    public string? VideoCodec { get; set; }
    public string? VideoProfile { get; set; }
    public int? VideoLevel { get; set; }
    public int? VideoHeight { get; set; }
    public double? VideoFrameRate { get; set; }
    public bool? VideoInterlaced { get; set; }
    public DateTime? VideoFormatSeenAt { get; set; }

    public IptvChannelKind Kind { get; set; }
    public bool KindOverriddenByAdmin { get; set; }

    public Guid PlaylistId { get; set; }
    public virtual IptvPlaylist Playlist { get; set; } = null!;
}
