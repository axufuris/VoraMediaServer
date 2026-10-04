using Vora.Domain.Entities.Iptv;

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

    public static IptvChannelVM FromEntity(IptvChannel channel, string playlistName) => new()
    {
        Id = channel.Id,
        PlaylistId = channel.PlaylistId,
        PlaylistName = playlistName,
        ExternalChannelId = channel.ExternalChannelId,
        Name = channel.Name,
        LogoUrl = channel.LogoUrl,
        GroupTitle = channel.GroupTitle,
        StreamUrl = channel.StreamUrl,
        Resolution = channel.Resolution,
        CountryCode = channel.CountryCode,
        IsHiddenByAdmin = channel.IsHiddenByAdmin,
        IsHealthy = channel.IsHealthy,
        LastHealthCheckAt = channel.LastHealthCheckAt,
        Kind = channel.Kind.ToString()
    };
}
