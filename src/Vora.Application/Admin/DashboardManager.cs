using Vora.Application.Admin.ViewModels;
using Vora.Application.Streaming;

namespace Vora.Application.Admin;

public interface IDashboardManager
{
    Task<DashboardFeedVM> GetDashboardFeedAsync();
}

public class DashboardManager(
    IStreamRepository streamRepository,
    ITranscodeService transcodeService) : IDashboardManager
{
    private const int MaxAllowedTranscodes = 3;
    private static readonly TimeSpan ActiveThreshold = TimeSpan.FromMinutes(2);

    public async Task<DashboardFeedVM> GetDashboardFeedAsync()
    {
        var activeLogs = await streamRepository.GetProjectedActiveStreamsAsync(ActiveThreshold, s => new
        {
            s.UserId,
            UserName = s.UserProfile != null ? s.UserProfile.Name : "Unknown User",
            MediaTitle = s.MediaItem.Title,
            ClientType = s.ClientDevice.ClientName,
            PlaybackState = s.IsPaused ? "Paused" : "Playing",
            s.CurrentPosition,
            s.MediaItemId
        });

        var activeStreams = activeLogs.Select(log => new ActiveStreamVM
        {
            UserId = log.UserId,
            UserName = log.UserName,
            MediaTitle = log.MediaTitle ?? "Unknown Media",
            ClientType = log.ClientType ?? "Unknown",
            PlaybackState = log.PlaybackState,
            IsTranscoding = transcodeService.IsMediaTranscoding(log.MediaItemId),
            CurrentPosition = TimeSpan.FromSeconds(log.CurrentPosition),
            MediaItemId = log.MediaItemId
        }).ToList();

        return new DashboardFeedVM
        {
            ActiveStreamCount = activeStreams.Count,
            ActiveTranscodeCount = transcodeService.GetActiveTranscodeCount(),
            MaxAllowedTranscodes = MaxAllowedTranscodes,
            ActiveStreams = activeStreams
        };
    }
}
