namespace Vora.Plugins.Dtos;

public class CollectionSyncItemDto
{
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string MediaType { get; set; } = "Movie";
}
