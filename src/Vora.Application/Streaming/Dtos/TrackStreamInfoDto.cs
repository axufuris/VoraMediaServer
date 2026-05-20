namespace Vora.Application.Streaming.Dtos;

public class TrackStreamInfoDto
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Codec { get; set; }
    public bool IsDefault { get; set; }
    public int? Channels { get; set; }
    public string? HdrType { get; set; }
}
