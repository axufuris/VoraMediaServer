namespace Vora.Application.YouTube.Dtos;

public class YouTubeChannelDto
{
    public string ChannelId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public long? SubscriberCount { get; set; }
    public long? VideoCount { get; set; }
    public string? UploadsPlaylistId { get; set; }
}
