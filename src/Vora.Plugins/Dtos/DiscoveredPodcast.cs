namespace Vora.Plugins.Dtos;

public class DiscoveredPodcast
{
    public required string Title { get; init; }
    public string? Author { get; init; }
    public required string FeedUrl { get; init; }
    public string? ArtworkUrl { get; init; }
    public string? Description { get; init; }
    public string? HomepageUrl { get; init; }
    public required string ProviderName { get; init; }
}
