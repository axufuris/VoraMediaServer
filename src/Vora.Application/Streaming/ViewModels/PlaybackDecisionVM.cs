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
    public string? SourceHdrType { get; set; }
    public int OutputHeight { get; set; }

    // Stream indexes inside the source file for the picked tracks. Used by
    // BuildFFmpegArguments to emit `-map 0:N` flags so FFmpeg uses the
    // exact tracks the user / decision manager chose, not the file's
    // default-flagged ones.
    public int? SelectedVideoStreamIndex { get; set; }
    public int? SelectedAudioStreamIndex { get; set; }
    public int? SelectedSubtitleStreamIndex { get; set; }

    // For VOD-style HLS: the full source duration drives how many segments
    // we list up-front in the playlist. The start position lets the
    // transcode begin from the user's last-watched position with the
    // correct segment numbering.
    public double SourceDurationSeconds { get; set; }
    public double StartPositionSeconds { get; set; }

    // The codec on the source's selected audio + video tracks, used by the
    // FFmpeg layer to override a "Copy" strategy when the source codec
    // can't be played back through mpegts (e.g. DTS-HD MA copy → silence
    // in ExoPlayer). Null when not yet resolved.
    public string? SourceAudioCodec { get; set; }
    public string? SourceVideoCodec { get; set; }
}
