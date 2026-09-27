namespace Vora.Domain.Entities.Users;

public class ProfileAccessSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public Guid UserProfileId { get; set; }
    public virtual UserProfile UserProfile { get; set; } = null!;
}
