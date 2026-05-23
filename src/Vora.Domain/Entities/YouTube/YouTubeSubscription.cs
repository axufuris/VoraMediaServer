namespace Vora.Domain.Entities.YouTube;

public class YouTubeSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserProfileId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string? ChannelThumbnailUrl { get; set; }
    public DateTimeOffset SubscribedAt { get; set; } = DateTimeOffset.UtcNow;
}
