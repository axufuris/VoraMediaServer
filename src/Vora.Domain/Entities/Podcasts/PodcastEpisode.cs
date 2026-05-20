namespace Vora.Domain.Entities.Podcasts;

public class PodcastEpisode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PodcastShowId { get; set; }
    public virtual PodcastShow Show { get; set; } = null!;

    public required string ExternalGuid { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string AudioUrl { get; set; }
    public string? ArtworkUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? SeasonNumber { get; set; }
}
