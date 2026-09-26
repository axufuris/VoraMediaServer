using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Media;

public enum GeneratedMixKind
{
    DailyMix = 0,
    DiscoverMix = 1,
    MoodMix = 2,
    ReleaseRadar = 3,

    // AI playlists. Kept off the ordinary mixes list, which older clients read,
    // and served from their own endpoint instead.
    AiPlaylist = 4,
    Bridge = 5,
    Blend = 6,
    Requested = 7
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

    // AI playlists only. Why this playlist suits the listener, in a sentence;
    // the words of a "Make me a playlist for..." request; and the other profile
    // in a Blend.
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public Guid? PartnerProfileId { get; set; }
}
