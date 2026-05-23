namespace Vora.Application.YouTube.Dtos;

public class YouTubeSearchPageDto
{
    public List<YouTubeVideoDto> Videos { get; set; } = new();
    public string? NextPageToken { get; set; }
}
