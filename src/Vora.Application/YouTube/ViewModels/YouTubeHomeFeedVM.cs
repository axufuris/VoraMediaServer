namespace Vora.Application.YouTube.ViewModels;

public class YouTubeHomeFeedVM
{
    public List<YouTubeContinueWatchingVM> ContinueWatching { get; set; } = new();
    public List<YouTubeVideoVM> FromSubscriptions { get; set; } = new();
    public List<YouTubeVideoVM> Trending { get; set; } = new();
    public List<YouTubeVideoVM> RecommendedForYou { get; set; } = new();
    public bool IsFreshState { get; set; }
}
