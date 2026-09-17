namespace Vora.Plugins.Dtos;

public class CollectionSyncItemDto
{
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string MediaType { get; set; } = "Movie";

    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? ShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
}
