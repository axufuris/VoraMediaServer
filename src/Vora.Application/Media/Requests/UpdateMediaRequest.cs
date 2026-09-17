namespace Vora.Application.Media.Requests;

public class UpdateMediaRequest
{
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public string? Overview { get; set; }
    public string? OriginalTitle { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Status { get; set; }
    public string? Tagline { get; set; }
    public string? HomePage { get; set; }
    public string? ContentRating { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackgroundUrl { get; set; }
    public List<string> LockedFields { get; set; } = new();
}