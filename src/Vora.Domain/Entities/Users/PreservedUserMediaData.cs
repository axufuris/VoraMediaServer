namespace Vora.Domain.Entities.Users;

public class PreservedUserMediaData
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public string ContentKey { get; set; } = string.Empty;
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;

    public decimal? Rating { get; set; }
    public DateTime? RatedAt { get; set; }

    public bool HasState { get; set; }
    public double ResumePositionSeconds { get; set; }
    public bool IsPlayed { get; set; }
    public bool IsHiddenFromContinueWatching { get; set; }
    public DateTime? LastPlayedAt { get; set; }
}
