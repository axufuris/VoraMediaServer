namespace Vora.Domain.Entities.Media;

public class TrackPlayHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Guid TrackId { get; set; }
    public virtual Track Track { get; set; } = null!;
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int DurationListenedSeconds { get; set; }
    public bool Completed { get; set; }
}
