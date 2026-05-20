namespace Vora.Application.Media.ViewModels;

public class SeasonVM
{
    public Guid Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Title { get; set; }
    public string? PosterUrl { get; set; }
    public int? EpisodeCount { get; set; }
    public bool IsPlayed { get; set; }
    public int? UnplayedItemCount { get; set; }
}