using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Podcasts;

public class PodcastEpisodeProfileState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public Guid PodcastEpisodeId { get; set; }
    public virtual PodcastEpisode Episode { get; set; } = null!;

    public double PositionSeconds { get; set; }
    public bool IsPlayed { get; set; }
    public DateTime LastListenedAt { get; set; } = DateTime.UtcNow;
}
