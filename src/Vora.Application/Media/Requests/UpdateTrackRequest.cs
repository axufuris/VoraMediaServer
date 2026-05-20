namespace Vora.Application.Media.Requests;

public class UpdateTrackRequest
{
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    public int TrackNumber { get; set; }
    public int? DiscNumber { get; set; }
    public string? ContentRating { get; set; }
    public List<string> LockedFields { get; set; } = new();
}
