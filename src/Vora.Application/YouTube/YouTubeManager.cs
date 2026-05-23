using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.YouTube.Dtos;
using Vora.Application.YouTube.Requests;
using Vora.Application.YouTube.ViewModels;
using Vora.Domain.Entities.YouTube;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.YouTube;

namespace Vora.Application.YouTube;

public interface IYouTubeManager
{
    Task<YouTubeProfileSettingsVM> GetProfileSettingsAsync(Guid profileId);
    Task<YouTubeProfileSettingsVM> UpdateProfileSettingsAsync(Guid profileId, UpdateYouTubeProfileSettingsRequest request);

    Task<YouTubeAccessResolution> EnsureAccessAsync(Guid profileId);

    Task<List<YouTubeVideoVM>> GetTrendingAsync(Guid profileId, CancellationToken ct = default);
    Task<List<YouTubeVideoVM>> SearchAsync(Guid profileId, string query, CancellationToken ct = default);
    Task<YouTubeSearchPageVM> SearchPageAsync(Guid profileId, string query, string? pageToken, CancellationToken ct = default);
    Task<YouTubeHomeFeedVM> GetHomeFeedAsync(Guid profileId, CancellationToken ct = default);

    Task<List<YouTubeSubscriptionVM>> GetSubscriptionsAsync(Guid profileId);
    Task<YouTubeSubscriptionVM> SubscribeAsync(Guid profileId, SubscribeToChannelRequest request, CancellationToken ct = default);
    Task UnsubscribeAsync(Guid profileId, string channelId);

    Task<List<YouTubeWatchHistoryVM>> GetWatchHistoryAsync(Guid profileId);
    Task RecordWatchAsync(Guid profileId, RecordWatchHistoryRequest request);
    Task ClearWatchHistoryAsync(Guid profileId);

    Task<YouTubeChannelVM?> GetChannelAsync(Guid profileId, string channelId, CancellationToken ct = default);
    Task<YouTubeSearchPageVM> GetChannelUploadsPageAsync(Guid profileId, string channelId, string? pageToken, CancellationToken ct = default);
    Task<List<YouTubePlaylistVM>> GetChannelPlaylistsAsync(Guid profileId, string channelId, CancellationToken ct = default);
    Task<YouTubeVideoVM?> GetVideoAsync(Guid profileId, string videoId, CancellationToken ct = default);

    Task<YouTubeStatusVM> GetAdminStatusAsync();
    Task<YouTubeAccountSettingsVM> GetAccountSettingsAsync(Guid accountId);
    Task<YouTubeAccountSettingsVM> UpdateAccountSettingsAsync(Guid accountId, UpdateYouTubeAccountSettingsRequest request);
}

public sealed class YouTubeManager(
    IYouTubeAccessResolver accessResolver,
    IYouTubeAccessRepository accessRepository,
    IYouTubeRepository repository,
    IYouTubeDataApiClient apiClient,
    IPluginSettingsProvider pluginSettings,
    IEnumerable<IVoraPlugin> plugins,
    IClientNotifier notifier,
    ILogger<YouTubeManager> logger) : IYouTubeManager
{
    private const int MaxSearchResults = 20;
    private const int MaxSubscriptionFeedItems = 50;
    private const int RecommendationSeedSize = 15;
    private const int MaxRecommendations = 25;

    public async Task<YouTubeProfileSettingsVM> GetProfileSettingsAsync(Guid profileId)
    {
        var resolution = await accessResolver.ResolveAsync(profileId);
        var stored = await accessRepository.GetProfileSettingsAsync(profileId);
        return new YouTubeProfileSettingsVM
        {
            IsEnabled = stored?.IsEnabled ?? true,
            IsAvailable = resolution.IsAvailable,
            UnavailableReason = resolution.DeniedReason
        };
    }

    public async Task<YouTubeProfileSettingsVM> UpdateProfileSettingsAsync(Guid profileId, UpdateYouTubeProfileSettingsRequest request)
    {
        await accessRepository.UpsertProfileSettingsAsync(new YouTubeProfileSettings
        {
            UserProfileId = profileId,
            IsEnabled = request.IsEnabled
        });
        return await GetProfileSettingsAsync(profileId);
    }

    public async Task<YouTubeAccessResolution> EnsureAccessAsync(Guid profileId)
    {
        var resolution = await accessResolver.ResolveAsync(profileId);
        if (!resolution.IsAvailable)
        {
            logger.LogInformation("YouTube access denied for profile {ProfileId}: {Reason}", profileId, resolution.DeniedReason);
            throw new UnauthorizedAccessException(resolution.DeniedReason ?? "YouTube is not available for this profile.");
        }
        return resolution;
    }

    public async Task<List<YouTubeVideoVM>> GetTrendingAsync(Guid profileId, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);
        var region = await GetTrendingRegionAsync();
        var trending = await apiClient.GetTrendingAsync(region, resolution.SafeSearch, ct);
        var filtered = ApplyRatingFilters(trending, resolution);
        return filtered.Select(MapVideo).ToList();
    }

    public async Task<List<YouTubeVideoVM>> SearchAsync(Guid profileId, string query, CancellationToken ct = default)
    {
        var page = await SearchPageAsync(profileId, query, null, ct);
        return page.Videos;
    }

    public async Task<YouTubeSearchPageVM> SearchPageAsync(Guid profileId, string query, string? pageToken, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(query)) return new YouTubeSearchPageVM();

        var page = await apiClient.SearchPageAsync(query, resolution.SafeSearch, MaxSearchResults, pageToken, ct);
        var filtered = ApplyRatingFilters(page.Videos, resolution);
        return new YouTubeSearchPageVM
        {
            Videos = filtered.Select(MapVideo).ToList(),
            NextPageToken = page.NextPageToken
        };
    }

    public async Task<YouTubeHomeFeedVM> GetHomeFeedAsync(Guid profileId, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);

        var continueWatching = await BuildContinueWatchingAsync(profileId);
        var subscriptions = await repository.GetSubscriptionsAsync(profileId);
        var subscribedChannelIds = new HashSet<string>(subscriptions.Select(s => s.ChannelId), StringComparer.Ordinal);

        var subscriptionsFeed = await BuildSubscriptionsFeedAsync(profileId, subscriptions, resolution, ct);
        var region = await GetTrendingRegionAsync();
        var trending = ApplyRatingFilters(await apiClient.GetTrendingAsync(region, resolution.SafeSearch, ct), resolution);

        var rivalVideoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in subscriptionsFeed) rivalVideoIds.Add(v.VideoId);
        foreach (var v in trending) rivalVideoIds.Add(v.VideoId);

        var recommended = await BuildRecommendationsAsync(profileId, resolution, subscribedChannelIds, rivalVideoIds, ct);

        return new YouTubeHomeFeedVM
        {
            ContinueWatching = continueWatching,
            FromSubscriptions = subscriptionsFeed.Select(MapVideo).ToList(),
            Trending = trending.Select(MapVideo).ToList(),
            RecommendedForYou = recommended.Select(MapVideo).ToList(),
            IsFreshState = continueWatching.Count == 0 && subscriptionsFeed.Count == 0 && recommended.Count == 0
        };
    }

    public async Task<List<YouTubeSubscriptionVM>> GetSubscriptionsAsync(Guid profileId)
    {
        await EnsureAccessAsync(profileId);
        var subs = await repository.GetSubscriptionsAsync(profileId);
        return subs.Select(MapSubscription).ToList();
    }

    public async Task<YouTubeSubscriptionVM> SubscribeAsync(Guid profileId, SubscribeToChannelRequest request, CancellationToken ct = default)
    {
        await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(request.ChannelId))
        {
            throw new ArgumentException("Channel id is required.", nameof(request));
        }

        var channel = await apiClient.GetChannelAsync(request.ChannelId, ct);
        if (channel is null)
        {
            throw new InvalidOperationException($"Channel '{request.ChannelId}' could not be resolved on YouTube.");
        }

        await repository.AddSubscriptionAsync(new YouTubeSubscription
        {
            UserProfileId = profileId,
            ChannelId = channel.ChannelId,
            ChannelName = channel.Title,
            ChannelThumbnailUrl = channel.ThumbnailUrl
        });

        var stored = await repository.GetSubscriptionAsync(profileId, channel.ChannelId);
        return stored is null ? new YouTubeSubscriptionVM { ChannelId = channel.ChannelId, ChannelName = channel.Title, ChannelThumbnailUrl = channel.ThumbnailUrl, SubscribedAt = DateTimeOffset.UtcNow } : MapSubscription(stored);
    }

    public async Task UnsubscribeAsync(Guid profileId, string channelId)
    {
        await EnsureAccessAsync(profileId);
        await repository.RemoveSubscriptionAsync(profileId, channelId);
    }

    public async Task<List<YouTubeWatchHistoryVM>> GetWatchHistoryAsync(Guid profileId)
    {
        await EnsureAccessAsync(profileId);
        var history = await repository.GetWatchHistoryAsync(profileId);
        return history.Select(MapWatchHistory).ToList();
    }

    public async Task RecordWatchAsync(Guid profileId, RecordWatchHistoryRequest request)
    {
        await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(request.VideoId))
        {
            throw new ArgumentException("Video id is required.", nameof(request));
        }

        await repository.RecordWatchAsync(new YouTubeWatchHistory
        {
            UserProfileId = profileId,
            VideoId = request.VideoId,
            VideoTitle = request.VideoTitle,
            ThumbnailUrl = request.ThumbnailUrl,
            ChannelId = request.ChannelId,
            ChannelName = request.ChannelName,
            DurationWatched = Math.Max(0, request.DurationWatched),
            TotalDuration = Math.Max(0, request.TotalDuration)
        });
    }

    public async Task ClearWatchHistoryAsync(Guid profileId)
    {
        await EnsureAccessAsync(profileId);
        await repository.ClearWatchHistoryAsync(profileId);
    }

    public async Task<YouTubeChannelVM?> GetChannelAsync(Guid profileId, string channelId, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(channelId)) return null;

        var channel = await apiClient.GetChannelAsync(channelId, ct);
        if (channel is null) return null;

        var uploads = await apiClient.GetChannelRecentUploadsAsync(channelId, 15, ct);
        var filteredUploads = ApplyRatingFilters(uploads, resolution);

        var subscription = await repository.GetSubscriptionAsync(profileId, channelId);

        return new YouTubeChannelVM
        {
            ChannelId = channel.ChannelId,
            Title = channel.Title,
            Description = channel.Description,
            ThumbnailUrl = channel.ThumbnailUrl,
            SubscriberCount = channel.SubscriberCount,
            VideoCount = channel.VideoCount,
            IsSubscribed = subscription is not null,
            RecentUploads = filteredUploads.Select(MapVideo).ToList()
        };
    }

    public async Task<YouTubeSearchPageVM> GetChannelUploadsPageAsync(Guid profileId, string channelId, string? pageToken, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(channelId)) return new YouTubeSearchPageVM();

        var page = await apiClient.GetChannelUploadsPageAsync(channelId, pageToken, 50, ct);
        var filtered = ApplyRatingFilters(page.Videos, resolution);
        return new YouTubeSearchPageVM
        {
            Videos = filtered.Select(MapVideo).ToList(),
            NextPageToken = page.NextPageToken
        };
    }

    public async Task<List<YouTubePlaylistVM>> GetChannelPlaylistsAsync(Guid profileId, string channelId, CancellationToken ct = default)
    {
        await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(channelId)) return new List<YouTubePlaylistVM>();

        var playlists = await apiClient.GetChannelPlaylistsAsync(channelId, 25, ct);
        return playlists.Select(p => new YouTubePlaylistVM
        {
            PlaylistId = p.PlaylistId,
            Title = p.Title,
            Description = p.Description,
            ThumbnailUrl = p.ThumbnailUrl,
            ItemCount = p.ItemCount,
            PublishedAt = p.PublishedAt,
            YouTubeUrl = $"https://www.youtube.com/playlist?list={p.PlaylistId}",
        }).ToList();
    }

    public async Task<YouTubeVideoVM?> GetVideoAsync(Guid profileId, string videoId, CancellationToken ct = default)
    {
        var resolution = await EnsureAccessAsync(profileId);
        if (string.IsNullOrWhiteSpace(videoId)) return null;

        var videos = await apiClient.GetVideosByIdAsync(new[] { videoId }, ct);
        var filtered = ApplyRatingFilters(videos, resolution);
        var first = filtered.FirstOrDefault();
        return first is null ? null : MapVideo(first);
    }

    public async Task<YouTubeStatusVM> GetAdminStatusAsync()
    {
        var pluginInstalled = plugins.Any(p => string.Equals(p.Id, YouTubePlugin.PluginId, StringComparison.OrdinalIgnoreCase));
        var configured = await apiClient.IsConfiguredAsync();
        var enabledRaw = await pluginSettings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.IsEnabledSettingKey);
        var enabled = !string.Equals(enabledRaw, "false", StringComparison.OrdinalIgnoreCase);

        return new YouTubeStatusVM
        {
            PluginInstalled = pluginInstalled,
            ApiKeyConfigured = configured,
            ServerEnabled = enabled,
            TrendingRegion = await GetTrendingRegionAsync()
        };
    }

    public async Task<YouTubeAccountSettingsVM> GetAccountSettingsAsync(Guid accountId)
    {
        var existing = await accessRepository.GetAccountSettingsAsync(accountId);
        return new YouTubeAccountSettingsVM
        {
            AccountId = accountId,
            YouTubeAccess = existing?.YouTubeAccess ?? YouTubeAccessSetting.Inherit,
            UpdatedAt = existing?.UpdatedAt
        };
    }

    public async Task<YouTubeAccountSettingsVM> UpdateAccountSettingsAsync(Guid accountId, UpdateYouTubeAccountSettingsRequest request)
    {
        await accessRepository.UpsertAccountSettingsAsync(new YouTubeAccountSettings
        {
            AccountId = accountId,
            YouTubeAccess = request.YouTubeAccess
        });
        await notifier.NotifyYouTubeAccessChangedAsync(accountId);
        return await GetAccountSettingsAsync(accountId);
    }

    private async Task<List<YouTubeContinueWatchingVM>> BuildContinueWatchingAsync(Guid profileId)
    {
        var rows = await repository.GetContinueWatchingAsync(profileId);
        return rows.Select(h => new YouTubeContinueWatchingVM
        {
            VideoId = h.VideoId,
            Title = h.VideoTitle,
            ThumbnailUrl = h.ThumbnailUrl,
            ChannelId = h.ChannelId,
            ChannelName = h.ChannelName,
            DurationWatched = h.DurationWatched,
            TotalDuration = h.TotalDuration,
            PercentComplete = h.TotalDuration > 0 ? Math.Min(1.0, (double)h.DurationWatched / h.TotalDuration) : 0.0,
            WatchedAt = h.WatchedAt
        }).ToList();
    }

    private async Task<List<YouTubeVideoDto>> BuildSubscriptionsFeedAsync(Guid profileId, IReadOnlyList<YouTubeSubscription> subs, YouTubeAccessResolution resolution, CancellationToken ct)
    {
        if (subs.Count == 0) return new List<YouTubeVideoDto>();

        var watched = await repository.GetWatchedVideoIdsAsync(profileId);
        var aggregated = new List<YouTubeVideoDto>();

        foreach (var sub in subs)
        {
            var uploads = await apiClient.GetChannelRecentUploadsAsync(sub.ChannelId, 10, ct);
            foreach (var upload in uploads)
            {
                if (string.IsNullOrWhiteSpace(upload.ChannelName) && !string.IsNullOrWhiteSpace(sub.ChannelName))
                {
                    upload.ChannelName = sub.ChannelName;
                }
            }
            aggregated.AddRange(uploads);
        }

        var filtered = ApplyRatingFilters(aggregated, resolution);

        return filtered
            .Where(v => !watched.Contains(v.VideoId))
            .GroupBy(v => v.VideoId)
            .Select(g => g.First())
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(MaxSubscriptionFeedItems)
            .ToList();
    }

    private async Task<List<YouTubeVideoDto>> BuildRecommendationsAsync(
        Guid profileId,
        YouTubeAccessResolution resolution,
        HashSet<string> subscribedChannelIds,
        HashSet<string> excludeVideoIds,
        CancellationToken ct)
    {
        var historyRows = await repository.GetWatchHistoryAsync(profileId, RecommendationSeedSize);
        if (historyRows.Count == 0) return new List<YouTubeVideoDto>();

        var watched = new HashSet<string>(historyRows.Select(h => h.VideoId), StringComparer.Ordinal);
        var aggregated = new List<YouTubeVideoDto>();

        foreach (var row in historyRows)
        {
            var related = await apiClient.GetRelatedVideosAsync(row.VideoId, resolution.SafeSearch, 5, ct);
            aggregated.AddRange(related);
        }

        var filtered = ApplyRatingFilters(aggregated, resolution);

        var rng = new Random();
        return filtered
            .Where(v => !watched.Contains(v.VideoId))
            .Where(v => !excludeVideoIds.Contains(v.VideoId))
            .Where(v => string.IsNullOrEmpty(v.ChannelId) || !subscribedChannelIds.Contains(v.ChannelId))
            .GroupBy(v => v.VideoId)
            .Select(g => g.First())
            .OrderBy(_ => rng.Next())
            .Take(MaxRecommendations)
            .ToList();
    }

    private async Task<string> GetTrendingRegionAsync()
    {
        var raw = await pluginSettings.GetSettingAsync(YouTubePlugin.PluginId, YouTubePlugin.TrendingRegionSettingKey);
        return string.IsNullOrWhiteSpace(raw) ? "US" : raw.Trim().ToUpperInvariant();
    }

    private static List<YouTubeVideoDto> ApplyRatingFilters(IEnumerable<YouTubeVideoDto> videos, YouTubeAccessResolution resolution)
    {
        var list = videos.ToList();

        if (resolution.FilterAgeRestricted)
        {
            list = list.Where(v => !string.Equals(v.YtRating, "ytAgeRestricted", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (resolution.HasAllRatings) return list;

        return list.Where(v => RatingPasses(v, resolution)).ToList();
    }

    private static bool RatingPasses(YouTubeVideoDto video, YouTubeAccessResolution resolution)
    {
        var movieRating = video.MpaaRating;
        var tvRating = video.TvpgRating;

        if (string.IsNullOrWhiteSpace(movieRating) && string.IsNullOrWhiteSpace(tvRating))
        {
            return !resolution.BlockUnratedContent;
        }

        if (!string.IsNullOrWhiteSpace(movieRating)
            && resolution.AllowedMovieRatings.Count > 0
            && !resolution.AllowedMovieRatings.Contains(movieRating, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tvRating)
            && resolution.AllowedTvRatings.Count > 0
            && !resolution.AllowedTvRatings.Contains(tvRating, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static YouTubeVideoVM MapVideo(YouTubeVideoDto dto) => new()
    {
        VideoId = dto.VideoId,
        Title = dto.Title,
        Description = dto.Description,
        ThumbnailUrl = dto.ThumbnailUrl,
        ChannelId = dto.ChannelId,
        ChannelName = dto.ChannelName,
        PublishedAt = dto.PublishedAt,
        ViewCount = dto.ViewCount,
        DurationSeconds = dto.DurationSeconds,
        EmbedWidth = dto.EmbedWidth,
        EmbedHeight = dto.EmbedHeight
    };

    private static YouTubeSubscriptionVM MapSubscription(YouTubeSubscription entity) => new()
    {
        ChannelId = entity.ChannelId,
        ChannelName = entity.ChannelName,
        ChannelThumbnailUrl = entity.ChannelThumbnailUrl,
        SubscribedAt = entity.SubscribedAt
    };

    private static YouTubeWatchHistoryVM MapWatchHistory(YouTubeWatchHistory entity) => new()
    {
        VideoId = entity.VideoId,
        VideoTitle = entity.VideoTitle,
        ThumbnailUrl = entity.ThumbnailUrl,
        ChannelId = entity.ChannelId,
        ChannelName = entity.ChannelName,
        DurationWatched = entity.DurationWatched,
        TotalDuration = entity.TotalDuration,
        WatchedAt = entity.WatchedAt
    };
}
