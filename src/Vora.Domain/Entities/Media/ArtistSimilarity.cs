namespace Vora.Domain.Entities.Media;

public class ArtistSimilarity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtistId { get; set; }
    public required string SimilarArtistName { get; set; }
    public double Score { get; set; }
    public string Source { get; set; } = "lastfm";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}

public class ArtistTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtistId { get; set; }
    public required string Tag { get; set; }
    public int Weight { get; set; }
    public string Source { get; set; } = "lastfm";
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
