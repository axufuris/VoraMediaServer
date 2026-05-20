namespace Vora.Application.Media.Requests;

public class UpdateAlbumRequest
{
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public string? ArtworkUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public string? DiscArtUrl { get; set; }
    public List<string> LockedFields { get; set; } = new();
}
