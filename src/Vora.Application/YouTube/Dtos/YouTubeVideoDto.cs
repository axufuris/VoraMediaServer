namespace Vora.Application.YouTube.Dtos;

public class YouTubeVideoDto
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
    public string? MpaaRating { get; set; }
    public string? TvpgRating { get; set; }
    public string? YtRating { get; set; }
    public int? EmbedWidth { get; set; }
    public int? EmbedHeight { get; set; }
}
