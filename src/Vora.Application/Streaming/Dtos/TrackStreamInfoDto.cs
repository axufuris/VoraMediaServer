namespace Vora.Application.Streaming.Dtos;

public class TrackStreamInfoDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public bool IsDefault { get; set; }
    public int? Channels { get; set; }
    public int? StreamIndex { get; set; }
    public string? HdrType { get; set; }
    public int? BitDepth { get; set; }
    public long? Bitrate { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? FrameRate { get; set; }
}
