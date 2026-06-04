namespace Vora.Application.Streaming.Dtos;

public class HistorySessionDto
{
    public string Id { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Player { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;

    public Guid LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;

    public string StartedAt { get; set; } = string.Empty;
    public int PausedMinutes { get; set; }
    public string StoppedAt { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int PercentComplete { get; set; }

    public string? OriginalVideoCodec { get; set; }
    public string? VideoCodec { get; set; }
    public string? OriginalAudioCodec { get; set; }
    public string? AudioCodec { get; set; }
    public int? OriginalAudioChannels { get; set; }
    public int? TargetAudioChannels { get; set; }
    public string? SubtitleStrategy { get; set; }
    public string? OriginalSubtitleCodec { get; set; }

    public int BandwidthKbps { get; set; }
    // Resolution + HDR. The "Source*" variants are the original media
    // and the "Output*" variants are what was delivered after any
    // transcode (downscale + tonemap). Both are exposed so the admin
    // Watch History row can show "4K HDR10 → 1080p SDR" for transcodes.
    public string? SourceResolution { get; set; }
    public string? SourceHdrType { get; set; }
    public string? OutputResolution { get; set; }
    public string? OutputHdrType { get; set; }
    public string? DecisionLog { get; set; }

    public bool IsGrouped { get; set; }
    public List<HistorySessionDto>? SubSessions { get; set; }
}
