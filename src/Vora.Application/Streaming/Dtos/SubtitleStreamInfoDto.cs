namespace Vora.Application.Streaming.Dtos;

public class SubtitleStreamInfoDto
{
    public Guid Id { get; set; }
    public string? Codec { get; set; }
    public string? Language { get; set; }
    public bool IsDefault { get; set; }
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
}
