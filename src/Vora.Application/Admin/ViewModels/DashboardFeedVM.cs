namespace Vora.Application.Admin.ViewModels;

public class DashboardFeedVM
{
    public int ActiveStreamCount { get; set; }
    public int ActiveTranscodeCount { get; set; }
    public int MaxAllowedTranscodes { get; set; }
    public IEnumerable<ActiveStreamVM> ActiveStreams { get; set; } = new List<ActiveStreamVM>();
}
