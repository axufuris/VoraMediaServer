namespace Vora.Application.YouTube.ViewModels;

public class YouTubeChannelVM
{
    public string ChannelId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long? SubscriberCount { get; set; }
    public long? VideoCount { get; set; }
    public bool IsSubscribed { get; set; }
    public List<YouTubeVideoVM> RecentUploads { get; set; } = new();
}
