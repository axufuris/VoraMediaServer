namespace Vora.Application.Iptv.Dtos;

public class IptvProgramDto
{
    public required string Id { get; set; }
    public required string ChannelId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string ContentRating { get; set; } = "NR";
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}
