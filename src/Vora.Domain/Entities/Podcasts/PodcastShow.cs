namespace Vora.Domain.Entities.Podcasts;

public class PodcastShow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FeedUrl { get; set; }
    public required string Title { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? HomepageUrl { get; set; }
    public string? Language { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRefreshedAt { get; set; }
    public string? LastError { get; set; }

    public bool IsInCatalog { get; set; }

    public virtual ICollection<PodcastEpisode> Episodes { get; set; } = new List<PodcastEpisode>();
    public virtual ICollection<PodcastSubscription> Subscriptions { get; set; } = new List<PodcastSubscription>();
}
