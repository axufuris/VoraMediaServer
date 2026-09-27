namespace Vora.Application.Streaming.Dtos;

public class MediaStreamInfoDto
{
    public Guid Id { get; set; }
    public List<MediaPartStreamInfoDto> Parts { get; set; } = new();
}