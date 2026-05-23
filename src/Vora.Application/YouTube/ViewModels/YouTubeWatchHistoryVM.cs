namespace Vora.Application.YouTube.ViewModels;

public class YouTubeWatchHistoryVM
{
    public string VideoId { get; set; } = string.Empty;
    public string VideoTitle { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public int DurationWatched { get; set; }
    public int TotalDuration { get; set; }
    public DateTimeOffset WatchedAt { get; set; }
}
