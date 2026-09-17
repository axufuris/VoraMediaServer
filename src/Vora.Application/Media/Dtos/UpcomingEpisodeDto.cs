namespace Vora.Application.Media.Dtos;

public class UpcomingEpisodeDto
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime AirDate { get; set; }
    public TimeSpan? AirTime { get; set; }
}
