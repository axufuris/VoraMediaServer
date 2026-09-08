namespace Vora.Application.Subtitles;

public class SubtitleExtractionTargetDto
{
    public Guid MediaItemId { get; set; }
    public Guid MediaPartId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public List<SubtitleTrackTargetDto> Tracks { get; set; } = new();
}

public class SubtitleTrackTargetDto
{
    public Guid Id { get; set; }
    public int StreamIndex { get; set; }
    public string? Codec { get; set; }
}
