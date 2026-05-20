namespace Vora.Application.Media.Requests;

public class UpdateSeasonRequest
{
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public List<string> LockedFields { get; set; } = new();
}