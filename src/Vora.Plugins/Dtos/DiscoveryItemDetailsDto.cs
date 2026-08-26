namespace Vora.Plugins.Dtos;

public class DiscoveryItemDetailsDto : DiscoveryItemDto
{
    public string? Overview { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? NextAirDate { get; set; }
    public int? RuntimeMinutes { get; set; }
    public decimal? Rating { get; set; }
    public List<string> Genres { get; set; } = new();
    public List<string> Studios { get; set; } = new();
    public List<CastMemberDto> Cast { get; set; } = new();
    public List<TrailerDto> Trailers { get; set; } = new();
}
