using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Podcasts;

public class PodcastSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public Guid PodcastShowId { get; set; }
    public virtual PodcastShow Show { get; set; } = null!;

    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
