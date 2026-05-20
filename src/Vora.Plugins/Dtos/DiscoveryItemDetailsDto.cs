namespace Vora.Plugins.Dtos;

public class DiscoveryItemDetailsDto : DiscoveryItemDto
{
    public string? Overview { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? NextAirDate { get; set; }
    public List<CastMemberDto> Cast { get; set; } = new();
    public List<TrailerDto> Trailers { get; set; } = new();
}
