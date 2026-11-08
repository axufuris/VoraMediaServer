namespace Vora.Application.Media.Requests;

public class ClearViewingHistoryRequest
{
    public bool WatchHistory { get; set; }
    public bool Ratings { get; set; }
}
