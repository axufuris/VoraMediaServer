namespace Vora.Application.Iptv.ViewModels;

public class IptvChannelVM
{
    public Guid Id { get; set; }
    public Guid PlaylistId { get; set; }
    public string PlaylistName { get; set; } = string.Empty;
    public string ExternalChannelId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? GroupTitle { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? CountryCode { get; set; }
    public bool IsHiddenByAdmin { get; set; }
    public bool? IsHealthy { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public string Kind { get; set; } = "Tv";
}
