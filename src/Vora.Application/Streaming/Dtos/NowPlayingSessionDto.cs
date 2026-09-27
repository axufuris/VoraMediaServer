namespace Vora.Application.Streaming.Dtos;

public class NowPlayingSessionDto
{
    public Guid SessionId { get; set; }
    public Guid MediaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TvShowTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? PosterUrl { get; set; }
    public double DurationSeconds { get; set; }

    public string ClientName { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public string SubtitleStrategy { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public int TargetAudioChannels { get; set; }
    public string Quality { get; set; } = string.Empty;
    public int BandwidthKbps { get; set; }
    public string? Resolution { get; set; }
    public string? HdrType { get; set; }
    // Delivered output (post-transcode): on a 4K HDR source being
    // downscaled + tonemapped to 1080p SDR, OutputResolution="1080p"
    // and OutputHdrType="SDR". Resolution/HdrType above remain the
    // source values so admins can still see what came in.
    public string? OutputResolution { get; set; }
    public string? OutputHdrType { get; set; }
    public string? DecisionLog { get; set; }

    public string? OriginalContainer { get; set; }
    public string? OriginalVideoCodec { get; set; }
    public string? OriginalAudioCodec { get; set; }
    public string? OriginalSubtitleCodec { get; set; }
    public int? OriginalAudioChannels { get; set; }

    public double CurrentPosition { get; set; }
    public bool IsPaused { get; set; }

    public string UserName { get; set; } = string.Empty;
}
