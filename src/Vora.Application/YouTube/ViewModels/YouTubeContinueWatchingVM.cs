namespace Vora.Application.YouTube.ViewModels;

public class YouTubeContinueWatchingVM
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public int DurationWatched { get; set; }
    public int TotalDuration { get; set; }
    public double PercentComplete { get; set; }
    public DateTimeOffset WatchedAt { get; set; }
}
