namespace Vora.Application.Iptv.ViewModels;

public class IptvPlaylistVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = "M3u";
    public string? M3uUrl { get; set; }
    public string? ServerUrl { get; set; }
    public string? Username { get; set; }
    public bool PasswordIsSet { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public bool UseProviderGuide { get; set; }
    public string? AccountStatus { get; set; }
    public DateTime? AccountExpiresAt { get; set; }
    public int? AccountMaxConnections { get; set; }
    public DateTime? AccountCheckedAt { get; set; }
    public bool IsActive { get; set; }
    public bool SupportsWebPlayback { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public int ConnectionLimit { get; set; }
    public string DefaultChannelKind { get; set; } = "Tv";
    public string? CountryFilter { get; set; }
    public bool EnableHealthCheck { get; set; }
    public List<IptvChannelVM> Channels { get; set; } = new();
}
