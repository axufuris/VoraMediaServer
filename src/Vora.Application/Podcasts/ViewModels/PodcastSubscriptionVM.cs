namespace Vora.Application.Podcasts.ViewModels;

public class PodcastSubscriptionVM
{
    public Guid Id { get; set; }
    public Guid ShowId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? HomepageUrl { get; set; }
    public DateTime SubscribedAt { get; set; }
    public DateTime? LastRefreshedAt { get; set; }
    public int EpisodeCount { get; set; }
}
