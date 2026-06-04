using System.Text.Json;
using Vora.Domain.Enums;

namespace Vora.Application.Streaming.Dtos;

public class StreamDecisionDto
{
    public StreamStrategy Strategy { get; set; }
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public string SubtitleStrategy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string TargetVideoCodec { get; set; } = string.Empty;
    public string TargetAudioCodec { get; set; } = string.Empty;
    public string TargetContainer { get; set; } = string.Empty;

    public Guid SelectedMediaPartId { get; set; }
    public Guid SelectedVideoTrackId { get; set; }
    public Guid SelectedAudioTrackId { get; set; }
    public int TargetAudioChannels { get; set; }
    public Guid? SelectedSubtitleTrackId { get; set; }
    public bool RequiresSubtitleBurnIn { get; set; }

    public string Quality { get; set; } = string.Empty;
    public int BandwidthKbps { get; set; }

    // What the user actually gets to see after the decision is executed.
    // Distinct from the source part's Resolution/HdrType because a 4K HDR
    // source under HdrTranscodeDownscale=Always becomes 1080p SDR output
    // even though the source is still 4K HDR. The badges + admin
    // Now Playing + Watch History rows surface these so the UI tells
    // the truth about what's being delivered.
    public string OutputResolution { get; set; } = string.Empty;
    public string OutputHdrType { get; set; } = string.Empty;

    public List<StreamOptionLogDto> EvaluatedOptions { get; set; } = new();

    public string GetDecisionLogJson() => JsonSerializer.Serialize(EvaluatedOptions);
}
