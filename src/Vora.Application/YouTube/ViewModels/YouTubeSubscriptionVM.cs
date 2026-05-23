namespace Vora.Application.YouTube.ViewModels;

public class YouTubeSubscriptionVM
{
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? ChannelThumbnailUrl { get; set; }
    public DateTimeOffset SubscribedAt { get; set; }
}
