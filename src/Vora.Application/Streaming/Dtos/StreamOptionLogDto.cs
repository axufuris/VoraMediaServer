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
}