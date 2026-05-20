namespace Vora.Domain.Entities.Discovery;

public class UserWatchlistItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? PosterUrl { get; set; }

    public string ExternalId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    public DateTime? ExpectedReleaseDate { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public Guid ProfileId { get; set; }
}
