namespace Vora.Plugins.Dtos;

// The items currently in a collection, handed to a chronology provider that
// orders the known set (e.g. AI) rather than fetching an external list.
public class CollectionOrderingItemDto
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string MediaType { get; set; } = "Movie";
    public string? TmdbId { get; set; }
    public string? ImdbId { get; set; }
}
