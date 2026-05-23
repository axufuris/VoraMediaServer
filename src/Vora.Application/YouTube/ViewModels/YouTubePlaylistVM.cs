namespace Vora.Application.YouTube.ViewModels;

public class YouTubePlaylistVM
{
    public string PlaylistId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public int? ItemCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string YouTubeUrl { get; set; } = string.Empty;
}
