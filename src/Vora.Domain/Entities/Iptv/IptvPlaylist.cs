using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Iptv;

public class IptvPlaylist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    public IptvSourceType SourceType { get; set; }

    public string? M3uUrl { get; set; }

    public string? ServerUrl { get; set; }
    public string? Username { get; set; }
    public string? PasswordCiphertext { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }

    public bool UseProviderGuide { get; set; } = true;

    public string? AccountStatus { get; set; }
    public DateTime? AccountExpiresAt { get; set; }
    public int? AccountMaxConnections { get; set; }
    public DateTime? AccountCheckedAt { get; set; }

    public bool IsActive { get; set; } = true;
    public bool SupportsWebPlayback { get; set; } = true;
    public string? LastError { get; set; }
    public DateTime? LastSyncedAt { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public IptvChannelKind DefaultChannelKind { get; set; }

    public string? CountryFilter { get; set; }

    public bool EnableHealthCheck { get; set; }

    public virtual IptvTunerProfile? TunerProfile { get; set; }
    public virtual ICollection<IptvChannel> Channels { get; set; } = new List<IptvChannel>();
}
