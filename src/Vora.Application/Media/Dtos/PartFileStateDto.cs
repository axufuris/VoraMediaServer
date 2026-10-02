namespace Vora.Application.Media.Dtos;

public class PartFileStateDto
{
    public Guid PartId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }
    public DateTime? LastAnalyzedAt { get; set; }
    public List<string> ExternalSubtitlePaths { get; set; } = new();
}
