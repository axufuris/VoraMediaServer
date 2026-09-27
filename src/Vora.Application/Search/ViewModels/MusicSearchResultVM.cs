namespace Vora.Application.Search.ViewModels;

public class MusicSearchResultVM
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ArtworkUrl { get; set; }
    public Guid? ArtistId { get; set; }
    public Guid? AlbumId { get; set; }
}
