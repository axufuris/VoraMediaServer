using Vora.Domain.Entities.Media;

namespace Vora.Domain.Entities.Users;

public class UserMediaState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double ResumePositionSeconds { get; set; }
    public bool IsPlayed { get; set; }
    public bool IsHiddenFromContinueWatching { get; set; }

    public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;
}
