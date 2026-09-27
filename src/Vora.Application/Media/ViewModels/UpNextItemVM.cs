namespace Vora.Application.Media.ViewModels;

public class UpNextItemVM
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? Overview { get; set; }
}
