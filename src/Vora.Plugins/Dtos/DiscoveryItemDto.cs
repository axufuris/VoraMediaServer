namespace Vora.Plugins.Dtos;

public class DiscoveryItemDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = "Movie";
    public int? Year { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? PosterUrl { get; set; }
    public string? ContentRating { get; set; }
}
