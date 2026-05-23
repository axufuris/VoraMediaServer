using Microsoft.EntityFrameworkCore;
using Vora.Application.YouTube;
using Vora.Domain.Entities.YouTube;

namespace Vora.Infrastructure.Persistence.Repositories;

public class YouTubeRepository(VoraDbContext context) : IYouTubeRepository
{
    public Task<List<YouTubeSubscription>> GetSubscriptionsAsync(Guid profileId) =>
        context.YouTubeSubscriptions
            .AsNoTracking()
            .Where(s => s.UserProfileId == profileId)
            .OrderBy(s => s.ChannelName)
            .ToListAsync();

    public Task<YouTubeSubscription?> GetSubscriptionAsync(Guid profileId, string channelId) =>
        context.YouTubeSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserProfileId == profileId && s.ChannelId == channelId);

    public async Task AddSubscriptionAsync(YouTubeSubscription subscription)
    {
        var exists = await context.YouTubeSubscriptions
            .AnyAsync(s => s.UserProfileId == subscription.UserProfileId && s.ChannelId == subscription.ChannelId);

        if (exists) return;

        subscription.SubscribedAt = DateTimeOffset.UtcNow;
        await context.YouTubeSubscriptions.AddAsync(subscription);
        await context.SaveChangesAsync();
    }

    public async Task RemoveSubscriptionAsync(Guid profileId, string channelId)
    {
        var existing = await context.YouTubeSubscriptions
            .FirstOrDefaultAsync(s => s.UserProfileId == profileId && s.ChannelId == channelId);
        if (existing is null) return;

        context.YouTubeSubscriptions.Remove(existing);
        await context.SaveChangesAsync();
    }

    public Task<List<YouTubeWatchHistory>> GetWatchHistoryAsync(Guid profileId, int take = 100) =>
        context.YouTubeWatchHistory
            .AsNoTracking()
            .Where(h => h.UserProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
            .Take(take)
            .ToListAsync();

    public async Task<List<YouTubeWatchHistory>> GetContinueWatchingAsync(Guid profileId, int take = 20)
    {
        var raw = await context.YouTubeWatchHistory
            .AsNoTracking()
            .Where(h => h.UserProfileId == profileId && h.TotalDuration > 0 && (double)h.DurationWatched / h.TotalDuration < 0.9)
            .OrderByDescending(h => h.WatchedAt)
            .Take(take * 4)
            .ToListAsync();

        return raw
            .GroupBy(h => h.VideoId)
            .Select(g => g.OrderByDescending(h => h.WatchedAt).First())
            .Take(take)
            .ToList();
    }

    public async Task<HashSet<string>> GetWatchedVideoIdsAsync(Guid profileId, int sampleSize = 500)
    {
        var ids = await context.YouTubeWatchHistory
            .AsNoTracking()
            .Where(h => h.UserProfileId == profileId)
            .OrderByDescending(h => h.WatchedAt)
            .Take(sampleSize)
            .Select(h => h.VideoId)
            .ToListAsync();

        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    public async Task RecordWatchAsync(YouTubeWatchHistory entry)
    {
        entry.WatchedAt = DateTimeOffset.UtcNow;
        await context.YouTubeWatchHistory.AddAsync(entry);
        await context.SaveChangesAsync();
    }

    public async Task ClearWatchHistoryAsync(Guid profileId)
    {
        await context.YouTubeWatchHistory
            .Where(h => h.UserProfileId == profileId)
            .ExecuteDeleteAsync();
    }
}
