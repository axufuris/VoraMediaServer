using Vora.Domain.Enums;

namespace Vora.Application.Iptv.Dtos;

public class IptvPlaylistSettingsDto
{
    public required string Name { get; init; }
    public IptvSourceType? SourceType { get; init; }
    public string? M3uUrl { get; init; }
    public string? ServerUrl { get; init; }
    public string? Username { get; init; }
    public string? NewPassword { get; init; }
    public bool ClearPassword { get; init; }
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public bool? UseProviderGuide { get; init; }
    public bool SupportsWebPlayback { get; init; }
    public int MaxConcurrentStreams { get; init; }
    public bool IsActive { get; init; } = true;
    public IptvChannelKind DefaultChannelKind { get; init; }
    public string? CountryFilter { get; init; }
    public bool EnableHealthCheck { get; init; }
}
