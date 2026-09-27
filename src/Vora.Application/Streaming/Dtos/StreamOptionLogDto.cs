namespace Vora.Application.Streaming.Dtos;

public class StreamOptionLogDto
{
    public Guid MediaPartId { get; set; }
    public int PenaltyScore { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string VideoStrategy { get; set; } = string.Empty;
    public string AudioStrategy { get; set; } = string.Empty;
    public string SubtitleStrategy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    // The three heights the resolution-fit score is built from. A client ceiling
    // that is not a rung the encoder can emit (a 1344p phone panel) used to be
    // scored as if it were, which made a 4K transcode look like a perfect fit
    // against a 1080p part that direct-plays. Seeing clientMax=1344,
    // deliverable=1080, output=1080 side by side makes the next report of that
    // class answer itself.
    public int ClientMaxHeight { get; set; }
    public int DeliverableHeight { get; set; }
    public int OutputHeight { get; set; }
}