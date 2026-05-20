namespace Vora.Application.Media.ViewModels;

public class DedupeItemVM
{
    public Guid PartId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public string HdrFormat { get; set; } = string.Empty;
    public string? AudioCodec { get; set; }
    public int? SampleRate { get; set; }
    public List<string> AudioTracks { get; set; } = new();
    public long QualityScore { get; set; }
    public string Container { get; set; } = string.Empty;
    public long? Bitrate { get; set; }
}
