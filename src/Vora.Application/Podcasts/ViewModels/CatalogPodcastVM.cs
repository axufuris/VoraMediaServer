namespace Vora.Application.Podcasts.ViewModels;

public class CatalogPodcastVM
{
    public Guid ShowId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string FeedUrl { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public string? HomepageUrl { get; set; }
    public bool IsSubscribed { get; set; }
}
