using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;

namespace Vora.Domain.Entities.Streaming;

public class StreamSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Strategy { get; set; } = string.Empty;
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public string SubtitleStrategy { get; set; } = string.Empty;

    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string Container { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? HdrType { get; set; }

    public int TargetAudioChannels { get; set; }
    public bool IsSubtitleBurnIn { get; set; }
    public int BandwidthKbps { get; set; }
    public string Quality { get; set; } = string.Empty;
    public string DecisionLog { get; set; } = string.Empty;

    public double StartPosition { get; set; }
    public double CurrentPosition { get; set; }
    public bool IsPaused { get; set; }
    public double TotalPausedDuration { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastPingAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }

    public Guid MediaItemId { get; set; }
    public virtual MediaItem MediaItem { get; set; } = null!;

    public Guid MediaPartId { get; set; }
    public Guid VideoTrackId { get; set; }
    public Guid AudioTrackId { get; set; }
    public Guid? SubtitleTrackId { get; set; }

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public Guid? UserProfileId { get; set; }
    public virtual UserProfile? UserProfile { get; set; }

    public Guid ClientDeviceId { get; set; }
    public virtual ClientDevice ClientDevice { get; set; } = null!;
}
