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

    public List<StreamOptionLogDto> EvaluatedOptions { get; set; } = new();

    public string GetDecisionLogJson() => JsonSerializer.Serialize(EvaluatedOptions);
}
