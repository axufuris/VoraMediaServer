namespace Vora.Application.Sync.ViewModels;

public class ContinueWatchingVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public double ResumePositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public Guid? TvShowId { get; set; }
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
}
