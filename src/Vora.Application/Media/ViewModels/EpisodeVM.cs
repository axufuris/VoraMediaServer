namespace Vora.Application.Media;

public class EpisodeVM
{
    public Guid Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public double? DurationMinutes { get; set; }
    public bool IsPlayed { get; set; }
    public double? ResumePositionSeconds { get; set; }
    public decimal? ServerAdminRating { get; set; }
    public decimal? MyRating { get; set; }
}