namespace Vora.Domain.Entities.Media;

public class TrackLike
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid TrackId { get; set; }
    public virtual Track Track { get; set; } = null!;
    public DateTime LikedAt { get; set; } = DateTime.UtcNow;
}
