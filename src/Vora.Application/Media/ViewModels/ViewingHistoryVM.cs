namespace Vora.Application.Media.ViewModels;

public class ViewingHistoryVM
{
    public int WatchedCount { get; set; }
    public int InProgressCount { get; set; }
    public int RatingCount { get; set; }
}

public class ClearedViewingHistoryVM
{
    public int WatchStatesRemoved { get; set; }
    public int RatingsRemoved { get; set; }
}
