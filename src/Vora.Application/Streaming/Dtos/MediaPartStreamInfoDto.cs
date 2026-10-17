namespace Vora.Application.Streaming.Dtos;

public class MediaPartStreamInfoDto
{
    public Guid Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public string? Edition { get; set; }
    public string? Container { get; set; }
    public long? OverallBitrate { get; set; }
    public List<TrackStreamInfoDto> VideoTracks { get; set; } = new();
    public List<TrackStreamInfoDto> AudioTracks { get; set; } = new();
    public List<SubtitleStreamInfoDto> SubtitleTracks { get; set; } = new();
}
