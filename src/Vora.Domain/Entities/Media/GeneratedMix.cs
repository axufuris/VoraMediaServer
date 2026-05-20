using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Media;

public enum GeneratedMixKind
{
    DailyMix = 0,
    DiscoverMix = 1,
    MoodMix = 2,
    ReleaseRadar = 3
}

public class GeneratedMix
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public int Slot { get; set; }
    public required string Name { get; set; }
    public string? DescriptionTag { get; set; }
    public GeneratedMixKind Kind { get; set; } = GeneratedMixKind.DailyMix;
    public string? ArtworkUrl { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastDriftAt { get; set; }

    public List<Guid> TrackOrder { get; set; } = new();
}
