namespace Vora.Domain.Entities.YouTube;

public class YouTubeWatchHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserProfileId { get; set; }
    public string VideoId { get; set; } = string.Empty;
    public string VideoTitle { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public int DurationWatched { get; set; }
    public int TotalDuration { get; set; }
    public DateTimeOffset WatchedAt { get; set; } = DateTimeOffset.UtcNow;
}
