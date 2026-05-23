namespace Vora.Application.YouTube.ViewModels;

public class YouTubeVideoVM
{
    public string VideoId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public long? ViewCount { get; set; }
    public int? DurationSeconds { get; set; }
    public int? EmbedWidth { get; set; }
    public int? EmbedHeight { get; set; }
}
