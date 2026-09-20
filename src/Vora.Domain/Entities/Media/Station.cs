using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Media;

public enum StationSeedKind
{
    Artist = 0,
    Track = 1,
    Genre = 2
}

public class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public required string Name { get; set; }
    public StationSeedKind SeedKind { get; set; }

    public Guid? SeedArtistId { get; set; }
    public Guid? SeedTrackId { get; set; }
    public string? SeedGenre { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedAt { get; set; }
}
