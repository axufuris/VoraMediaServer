using Vora.Domain.Enums;

namespace Vora.Application.Streaming.ViewModels;

public class PlaybackDecisionVM
{
    public Guid MediaItemId { get; set; }
    public StreamingState Decision { get; set; }
    public VideoCodec TargetVideoCodec { get; set; }
    public AudioCodec TargetAudioCodec { get; set; }
    public string TargetContainer { get; set; } = string.Empty;
    public bool RequiresSubtitleBurnIn { get; set; }
    public string TranscodeReason { get; set; } = string.Empty;
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public int BandwidthKbps { get; set; }
    public int TargetAudioChannels { get; set; }
}
