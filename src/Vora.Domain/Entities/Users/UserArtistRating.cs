using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Users;

public class UserArtistRating
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public decimal Rating { get; set; }

    public DateTime RatedAt { get; set; } = DateTime.UtcNow;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public Guid ArtistId { get; set; }
    public virtual Artist Artist { get; set; } = null!;
}
