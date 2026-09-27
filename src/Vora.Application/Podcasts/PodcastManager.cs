using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Vora.Application.Analysis;
using Vora.Application.Podcasts.ViewModels;
using Vora.Domain.Entities.Podcasts;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Podcasts;

public interface IPodcastManager
{
    Task<List<PodcastSubscriptionVM>> GetSubscriptionsAsync(Guid profileId);
    Task<PodcastSubscriptionVM> SubscribeAsync(Guid profileId, string feedUrl, bool canAddCustomFeeds);
    Task UnsubscribeAsync(Guid profileId, Guid subscriptionId);
    Task RefreshSubscriptionAsync(Guid profileId, Guid subscriptionId);
    Task RefreshShowAsync(Guid showId);
    Task<List<PodcastEpisodeVM>> GetEpisodesAsync(Guid profileId, Guid subscriptionId, int limit);
    Task SaveEpisodeStateAsync(Guid profileId, Guid episodeId, double positionSeconds, bool? explicitIsPlayed);
    Task<List<DiscoveredPodcastVM>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
    Task<List<PodcastFeedEpisodeVM>> GetRecentEpisodesAsync(Guid profileId, int limit, int? daysBack);
    Task<List<CatalogPodcastVM>> GetCatalogAsync(Guid profileId);
    Task<CatalogPodcastVM> AddToCatalogAsync(string feedUrl);
    Task RemoveFromCatalogAsync(Guid showId);
}

public class PodcastPermissionDeniedException : Exception
{
    public PodcastPermissionDeniedException(string message) : base(message) { }
}

public class PodcastManager : IPodcastManager
{
    public const string HttpClientName = "PodcastHttpClient";
    private const int DefaultEpisodeLimit = 100;
    private const int MaxFeedSizeBytes = 10 * 1024 * 1024;

    private readonly IPodcastRepository _repository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClientNotifier _notifier;
    private readonly IEnumerable<IPodcastDiscoveryProvider> _discoveryProviders;
    private readonly ILogger<PodcastManager> _logger;

    public PodcastManager(
        IPodcastRepository repository,
        IHttpClientFactory httpClientFactory,
        IClientNotifier notifier,
        IEnumerable<IPodcastDiscoveryProvider> discoveryProviders,
        ILogger<PodcastManager> logger)
    {
        _repository = repository;
        _httpClientFactory = httpClientFactory;
        _notifier = notifier;
        _discoveryProviders = discoveryProviders;
        _logger = logger;
    }

    public async Task<List<PodcastSubscriptionVM>> GetSubscriptionsAsync(Guid profileId)
    {
        var subscriptions = await _repository.GetSubscriptionsForProfileAsync(profileId);
        var showIds = subscriptions.Select(s => s.PodcastShowId).Distinct().ToList();
        var counts = await _repository.GetEpisodeCountsAsync(showIds);

        return subscriptions.Select(s => MapSubscription(s, counts)).ToList();
    }

    public async Task<PodcastSubscriptionVM> SubscribeAsync(Guid profileId, string feedUrl, bool canAddCustomFeeds)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new InvalidOperationException("Feed URL is required.");
        }

        var trimmedUrl = feedUrl.Trim();

        if (!canAddCustomFeeds)
        {
            var inCatalog = await _repository.IsShowInCatalogAsync(trimmedUrl);
            if (!inCatalog)
            {
                throw new PodcastPermissionDeniedException("This profile can only subscribe to podcasts from the server catalog.");
            }
        }

        var show = await _repository.GetShowByFeedUrlAsync(trimmedUrl);

        var notifyNewEpisodes = false;
        if (show == null)
        {
            var feed = await FetchAndParseFeedAsync(trimmedUrl);
            show = new PodcastShow
            {
                FeedUrl = trimmedUrl,
                Title = Truncate(feed.Title, 500),
                Author = TruncateOrNull(feed.Author, 256),
                Description = TruncateOrNull(feed.Description, 4000),
                ArtworkUrl = TruncateOrNull(feed.ArtworkUrl, 2048),
                HomepageUrl = TruncateOrNull(feed.HomepageUrl, 2048),
                Language = TruncateOrNull(feed.Language, 16),
                LastRefreshedAt = DateTime.UtcNow
            };
            await _repository.AddShowAsync(show);
            var addedCount = await UpsertFeedEpisodesAsync(show.Id, feed);
            notifyNewEpisodes = addedCount > 0;
        }
        else
        {
            try
            {
                var feed = await FetchAndParseFeedAsync(trimmedUrl);
                show.Title = Truncate(feed.Title, 500);
                show.Author = TruncateOrNull(feed.Author, 256);
                show.Description = TruncateOrNull(feed.Description, 4000);
                show.ArtworkUrl = TruncateOrNull(feed.ArtworkUrl, 2048);
                show.HomepageUrl = TruncateOrNull(feed.HomepageUrl, 2048);
                show.Language = TruncateOrNull(feed.Language, 16);
                show.LastRefreshedAt = DateTime.UtcNow;
                show.LastError = null;
                await _repository.UpdateShowAsync(show);
                var addedCount = await UpsertFeedEpisodesAsync(show.Id, feed);
                notifyNewEpisodes = addedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh existing show on subscribe for {FeedUrl}; keeping cached data.", trimmedUrl);
            }
        }

        if (notifyNewEpisodes)
        {
            await _notifier.NotifyPodcastEpisodesUpdatedAsync(show.Id);
        }

        var existingSubs = await _repository.GetSubscriptionsForProfileAsync(profileId);
        var existing = existingSubs.FirstOrDefault(s => s.PodcastShowId == show.Id);
        if (existing != null)
        {
            var counts = await _repository.GetEpisodeCountsAsync(new[] { show.Id });
            return MapSubscription(existing, counts);
        }

        var subscription = new PodcastSubscription
        {
            ProfileId = profileId,
            PodcastShowId = show.Id
        };
        await _repository.AddSubscriptionAsync(subscription);
        subscription.Show = show;

        var finalCounts = await _repository.GetEpisodeCountsAsync(new[] { show.Id });
        return MapSubscription(subscription, finalCounts);
    }

    public async Task UnsubscribeAsync(Guid profileId, Guid subscriptionId)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(subscriptionId);
        if (subscription == null || subscription.ProfileId != profileId) return;

        var showId = subscription.PodcastShowId;
        await _repository.RemoveSubscriptionAsync(subscription);

        var stillSubscribed = await _repository.ProfileHasOtherSubscriptionsAsync(showId, profileId);
        if (stillSubscribed) return;

        var show = await _repository.GetShowByIdAsync(showId);
        if (show != null && !show.IsInCatalog)
        {
            await _repository.DeleteShowAsync(showId);
        }
    }

    public async Task RefreshSubscriptionAsync(Guid profileId, Guid subscriptionId)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(subscriptionId);
        if (subscription == null || subscription.ProfileId != profileId)
        {
            throw new InvalidOperationException("Subscription not found.");
        }
        await RefreshShowAsync(subscription.PodcastShowId);
    }

    public async Task RefreshShowAsync(Guid showId)
    {
        var show = await _repository.GetShowByIdAsync(showId);
        if (show == null)
        {
            throw new InvalidOperationException("Show not found.");
        }

        try
        {
            var feed = await FetchAndParseFeedAsync(show.FeedUrl);
            show.Title = Truncate(feed.Title, 500);
            show.Author = TruncateOrNull(feed.Author, 256);
            show.Description = TruncateOrNull(feed.Description, 4000);
            show.ArtworkUrl = TruncateOrNull(feed.ArtworkUrl, 2048);
            show.HomepageUrl = TruncateOrNull(feed.HomepageUrl, 2048);
            show.Language = TruncateOrNull(feed.Language, 16);
            show.LastRefreshedAt = DateTime.UtcNow;
            show.LastError = null;
            await _repository.UpdateShowAsync(show);
            var newCount = await UpsertFeedEpisodesAsync(show.Id, feed);
            if (newCount > 0)
            {
                await _notifier.NotifyPodcastEpisodesUpdatedAsync(show.Id);
            }
        }
        catch (Exception ex)
        {
            show.LastError = ex.Message.Length > 1024 ? ex.Message[..1024] : ex.Message;
            show.LastRefreshedAt = DateTime.UtcNow;
            await _repository.UpdateShowAsync(show);
            throw;
        }
    }

    public async Task<List<PodcastEpisodeVM>> GetEpisodesAsync(Guid profileId, Guid subscriptionId, int limit)
    {
        var subscription = await _repository.GetSubscriptionByIdAsync(subscriptionId);
        if (subscription == null || subscription.ProfileId != profileId)
        {
            return new List<PodcastEpisodeVM>();
        }

        var effectiveLimit = limit <= 0 ? DefaultEpisodeLimit : Math.Min(limit, 500);
        var episodes = await _repository.GetEpisodesForShowAsync(subscription.PodcastShowId, effectiveLimit);
        var states = await _repository.GetStatesForShowAsync(profileId, subscription.PodcastShowId);

        return episodes.Select(e =>
        {
            states.TryGetValue(e.Id, out var state);
            return new PodcastEpisodeVM
            {
                Id = e.Id,
                ShowId = e.PodcastShowId,
                Title = e.Title,
                Description = e.Description,
                AudioUrl = e.AudioUrl,
                ArtworkUrl = e.ArtworkUrl,
                DurationSeconds = e.DurationSeconds,
                PublishedAt = e.PublishedAt,
                EpisodeNumber = e.EpisodeNumber,
                SeasonNumber = e.SeasonNumber,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsPlayed = state?.IsPlayed ?? false
            };
        }).ToList();
    }

    public async Task SaveEpisodeStateAsync(Guid profileId, Guid episodeId, double positionSeconds, bool? explicitIsPlayed)
    {
        var episode = await _repository.GetEpisodeByIdAsync(episodeId);
        if (episode == null)
        {
            throw new InvalidOperationException("Episode not found.");
        }

        var subscribed = await _repository.ProfileSubscribesToShowAsync(profileId, episode.PodcastShowId);
        if (!subscribed)
        {
            throw new UnauthorizedAccessException("Profile is not subscribed to this show.");
        }

        var clampedPosition = positionSeconds < 0 ? 0 : positionSeconds;
        if (episode.DurationSeconds is { } duration && duration > 0)
        {
            clampedPosition = Math.Min(clampedPosition, duration);
        }

        bool isPlayed;
        if (explicitIsPlayed.HasValue)
        {
            isPlayed = explicitIsPlayed.Value;
        }
        else if (episode.DurationSeconds is { } d && d > 0 && clampedPosition >= d - 30)
        {
            isPlayed = true;
        }
        else
        {
            var existing = await _repository.GetStateAsync(profileId, episodeId);
            isPlayed = existing?.IsPlayed ?? false;
        }

        await _repository.UpsertStateAsync(profileId, episodeId, clampedPosition, isPlayed);
    }

    public async Task<List<PodcastFeedEpisodeVM>> GetRecentEpisodesAsync(Guid profileId, int limit, int? daysBack)
    {
        var effectiveLimit = limit <= 0 ? 50 : Math.Min(limit, 200);
        var subscriptions = await _repository.GetSubscriptionsForProfileAsync(profileId);
        if (subscriptions.Count == 0) return new List<PodcastFeedEpisodeVM>();

        var subByShowId = subscriptions.ToDictionary(s => s.PodcastShowId);
        var showIds = subByShowId.Keys.ToList();

        DateTime? since = daysBack.HasValue && daysBack.Value > 0
            ? DateTime.UtcNow.AddDays(-daysBack.Value)
            : null;

        var episodes = await _repository.GetEpisodesAcrossShowsAsync(showIds, since, effectiveLimit);
        if (episodes.Count == 0) return new List<PodcastFeedEpisodeVM>();

        var states = await _repository.GetStatesForEpisodesAsync(profileId, episodes.Select(e => e.Id));

        return episodes.Select(e =>
        {
            states.TryGetValue(e.Id, out var state);
            subByShowId.TryGetValue(e.PodcastShowId, out var subscription);
            return new PodcastFeedEpisodeVM
            {
                Id = e.Id,
                ShowId = e.PodcastShowId,
                SubscriptionId = subscription?.Id ?? Guid.Empty,
                ShowTitle = subscription?.Show.Title ?? string.Empty,
                ShowArtworkUrl = subscription?.Show.ArtworkUrl,
                Title = e.Title,
                Description = e.Description,
                AudioUrl = e.AudioUrl,
                ArtworkUrl = e.ArtworkUrl,
                DurationSeconds = e.DurationSeconds,
                PublishedAt = e.PublishedAt,
                EpisodeNumber = e.EpisodeNumber,
                SeasonNumber = e.SeasonNumber,
                PositionSeconds = state?.PositionSeconds ?? 0,
                IsPlayed = state?.IsPlayed ?? false
            };
        }).ToList();
    }

    public async Task<List<CatalogPodcastVM>> GetCatalogAsync(Guid profileId)
    {
        var shows = await _repository.GetCatalogShowsAsync();
        if (shows.Count == 0) return new List<CatalogPodcastVM>();

        var subscriptions = await _repository.GetSubscriptionsForProfileAsync(profileId);
        var subscribedShowIds = subscriptions.Select(s => s.PodcastShowId).ToHashSet();

        return shows.Select(s => new CatalogPodcastVM
        {
            ShowId = s.Id,
            Title = s.Title,
            Author = s.Author,
            Description = s.Description,
            FeedUrl = s.FeedUrl,
            ArtworkUrl = s.ArtworkUrl,
            HomepageUrl = s.HomepageUrl,
            IsSubscribed = subscribedShowIds.Contains(s.Id)
        }).ToList();
    }

    public async Task<CatalogPodcastVM> AddToCatalogAsync(string feedUrl)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            throw new InvalidOperationException("Feed URL is required.");
        }

        var trimmedUrl = feedUrl.Trim();
        var show = await _repository.GetShowByFeedUrlAsync(trimmedUrl);

        if (show == null)
        {
            var feed = await FetchAndParseFeedAsync(trimmedUrl);
            show = new PodcastShow
            {
                FeedUrl = trimmedUrl,
                Title = Truncate(feed.Title, 500),
                Author = TruncateOrNull(feed.Author, 256),
                Description = TruncateOrNull(feed.Description, 4000),
                ArtworkUrl = TruncateOrNull(feed.ArtworkUrl, 2048),
                HomepageUrl = TruncateOrNull(feed.HomepageUrl, 2048),
                Language = TruncateOrNull(feed.Language, 16),
                LastRefreshedAt = DateTime.UtcNow,
                IsInCatalog = true
            };
            await _repository.AddShowAsync(show);
            await UpsertFeedEpisodesAsync(show.Id, feed);
        }
        else if (!show.IsInCatalog)
        {
            show.IsInCatalog = true;
            await _repository.UpdateShowAsync(show);
        }

        return new CatalogPodcastVM
        {
            ShowId = show.Id,
            Title = show.Title,
            Author = show.Author,
            Description = show.Description,
            FeedUrl = show.FeedUrl,
            ArtworkUrl = show.ArtworkUrl,
            HomepageUrl = show.HomepageUrl,
            IsSubscribed = false
        };
    }

    public async Task RemoveFromCatalogAsync(Guid showId)
    {
        var show = await _repository.GetShowByIdAsync(showId);
        if (show == null || !show.IsInCatalog) return;

        show.IsInCatalog = false;
        await _repository.UpdateShowAsync(show);

        var hasSubscribers = await _repository.ProfileHasOtherSubscriptionsAsync(showId, Guid.Empty);
        if (!hasSubscribers)
        {
            await _repository.DeleteShowAsync(showId);
        }
    }

    public async Task<List<DiscoveredPodcastVM>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<DiscoveredPodcastVM>();

        var effectiveLimit = limit <= 0 ? 25 : Math.Min(limit, 100);
        var providers = _discoveryProviders.ToList();
        if (providers.Count == 0) return new List<DiscoveredPodcastVM>();

        var tasks = providers.Select(async provider =>
        {
            try
            {
                return await provider.SearchAsync(query, effectiveLimit, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Podcast discovery provider {Provider} failed for query {Query}", provider.ProviderName, query);
                return (IReadOnlyList<DiscoveredPodcast>)Array.Empty<DiscoveredPodcast>();
            }
        });

        var results = await Task.WhenAll(tasks);

        var merged = new List<DiscoveredPodcastVM>();
        var seenFeedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitleAuthor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var providerResults in results)
        {
            foreach (var item in providerResults)
            {
                if (string.IsNullOrWhiteSpace(item.FeedUrl)) continue;
                if (!seenFeedUrls.Add(item.FeedUrl)) continue;

                var titleAuthorKey = $"{item.Title?.Trim()}|{item.Author?.Trim()}";
                if (!seenTitleAuthor.Add(titleAuthorKey)) continue;

                merged.Add(new DiscoveredPodcastVM
                {
                    Title = item.Title ?? string.Empty,
                    Author = item.Author,
                    FeedUrl = item.FeedUrl,
                    ArtworkUrl = item.ArtworkUrl,
                    Description = item.Description,
                    HomepageUrl = item.HomepageUrl,
                    ProviderName = item.ProviderName
                });

                if (merged.Count >= effectiveLimit) return merged;
            }
        }

        return merged;
    }

    private async Task<PodcastFeedResult> FetchAndParseFeedAsync(string feedUrl)
    {
        await EnsureFeedUrlIsSafeAsync(feedUrl);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(feedUrl);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } len && len > MaxFeedSizeBytes)
        {
            throw new InvalidOperationException($"Feed is too large ({len} bytes).");
        }

        var xml = await response.Content.ReadAsStringAsync();
        return PodcastFeedParser.Parse(xml);
    }

    private static async Task EnsureFeedUrlIsSafeAsync(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The feed URL must be a valid http or https address.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost);
        }
        catch
        {
            throw new InvalidOperationException("The feed URL could not be resolved.");
        }

        if (addresses.Length == 0 || addresses.Any(IsDisallowedAddress))
        {
            throw new InvalidOperationException("The feed URL points to a disallowed address.");
        }
    }

    private static bool IsDisallowedAddress(IPAddress address)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return true;
            if (b[0] == 10) return true;
            if (b[0] == 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            return false;
        }

        return true;
    }

    private async Task<int> UpsertFeedEpisodesAsync(Guid showId, PodcastFeedResult feed)
    {
        var episodes = feed.Episodes.Select(e => new PodcastEpisode
        {
            PodcastShowId = showId,
            ExternalGuid = Truncate(e.Guid, 512),
            Title = Truncate(e.Title, 500),
            Description = TruncateOrNull(e.Description, 8000),
            AudioUrl = Truncate(e.AudioUrl, 2048),
            ArtworkUrl = TruncateOrNull(e.ArtworkUrl, 2048),
            DurationSeconds = e.DurationSeconds,
            PublishedAt = e.PublishedAt,
            EpisodeNumber = e.EpisodeNumber,
            SeasonNumber = e.SeasonNumber
        }).ToList();

        return await _repository.UpsertEpisodesAsync(showId, episodes);
    }

    private static PodcastSubscriptionVM MapSubscription(PodcastSubscription sub, Dictionary<Guid, int> counts) =>
        new()
        {
            Id = sub.Id,
            ShowId = sub.PodcastShowId,
            Title = sub.Show.Title,
            Author = sub.Show.Author,
            Description = sub.Show.Description,
            ArtworkUrl = sub.Show.ArtworkUrl,
            HomepageUrl = sub.Show.HomepageUrl,
            SubscribedAt = sub.SubscribedAt,
            LastRefreshedAt = sub.Show.LastRefreshedAt,
            EpisodeCount = counts.TryGetValue(sub.PodcastShowId, out var count) ? count : 0
        };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? TruncateOrNull(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : (value.Length <= max ? value : value[..max]);
}
