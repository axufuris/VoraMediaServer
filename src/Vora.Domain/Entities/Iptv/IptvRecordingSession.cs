using Vora.Domain.Enums;

namespace Vora.Domain.Entities.Iptv;

public class IptvRecordingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public string? EpisodeTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }

    public string? ExternalProgramId { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public IptvRecordingSessionStatus Status { get; set; } = IptvRecordingSessionStatus.Pending;
    public string? ErrorMessage { get; set; }

    public string? OutputFilePath { get; set; }
    public long FileSizeBytes { get; set; }
    public string? CommercialMarkersJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Guid ScheduleId { get; set; }
    public virtual IptvRecordingSchedule Schedule { get; set; } = null!;
}
