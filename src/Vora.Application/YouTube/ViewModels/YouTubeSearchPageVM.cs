namespace Vora.Application.YouTube.ViewModels;

public class YouTubeSearchPageVM
{
    public List<YouTubeVideoVM> Videos { get; set; } = new();
    public string? NextPageToken { get; set; }
}
