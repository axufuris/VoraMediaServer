using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Iptv;

public class IptvRecordingSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    public string? ProgramId { get; set; }
    public bool IsSeriesRecording { get; set; }

    public int KeepMaxEpisodes { get; set; }
    public bool DeleteAfterWatching { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public Guid ProfileId { get; set; }
    public virtual UserProfile Profile { get; set; } = null!;

    public Guid ChannelId { get; set; }
    public virtual IptvChannel Channel { get; set; } = null!;

    public virtual ICollection<IptvRecordingSession> Sessions { get; set; } = new List<IptvRecordingSession>();
}
