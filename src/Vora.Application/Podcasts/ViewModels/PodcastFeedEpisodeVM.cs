namespace Vora.Application.Podcasts.ViewModels;

public class PodcastFeedEpisodeVM
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string ShowTitle { get; set; } = string.Empty;
    public string? ShowArtworkUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AudioUrl { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? SeasonNumber { get; set; }
    public double PositionSeconds { get; set; }
    public bool IsPlayed { get; set; }
}
