using Vora.Domain.Entities.YouTube;

namespace Vora.Application.YouTube;

public interface IYouTubeRepository
{
    Task<List<YouTubeSubscription>> GetSubscriptionsAsync(Guid profileId);
    Task<YouTubeSubscription?> GetSubscriptionAsync(Guid profileId, string channelId);
    Task AddSubscriptionAsync(YouTubeSubscription subscription);
    Task RemoveSubscriptionAsync(Guid profileId, string channelId);

    Task<List<YouTubeWatchHistory>> GetWatchHistoryAsync(Guid profileId, int take = 100);
    Task<List<YouTubeWatchHistory>> GetContinueWatchingAsync(Guid profileId, int take = 20);
    Task<HashSet<string>> GetWatchedVideoIdsAsync(Guid profileId, int sampleSize = 500);
    Task RecordWatchAsync(YouTubeWatchHistory entry);
    Task ClearWatchHistoryAsync(Guid profileId);
}
