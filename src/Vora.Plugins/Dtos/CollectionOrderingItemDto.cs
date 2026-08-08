namespace Vora.Plugins.Dtos;

public class CollectionOrderingItemDto
{
    public int Index { get; set; }
    public Guid LocalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = "Movie";
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
}
