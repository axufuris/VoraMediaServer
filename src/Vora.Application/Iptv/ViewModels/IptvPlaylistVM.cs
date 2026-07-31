namespace Vora.Application.Iptv.ViewModels;

public class IptvPlaylistVM
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? M3uUrl { get; set; }
    public bool IsActive { get; set; }
    public bool SupportsWebPlayback { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public string DefaultChannelKind { get; set; } = "Tv";
    public string? CountryFilter { get; set; }
    public List<IptvChannelVM> Channels { get; set; } = new();
}
