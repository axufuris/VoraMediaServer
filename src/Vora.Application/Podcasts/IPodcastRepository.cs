using Vora.Domain.Entities.Podcasts;

namespace Vora.Application.Podcasts;

public interface IPodcastRepository
{
    Task<List<PodcastSubscription>> GetSubscriptionsForProfileAsync(Guid profileId);
    Task<PodcastSubscription?> GetSubscriptionByIdAsync(Guid subscriptionId);
    Task<PodcastShow?> GetShowByFeedUrlAsync(string feedUrl);
    Task<PodcastShow?> GetShowByIdAsync(Guid showId);
    Task<Dictionary<Guid, int>> GetEpisodeCountsAsync(IEnumerable<Guid> showIds);
    Task AddShowAsync(PodcastShow show);
    Task UpdateShowAsync(PodcastShow show);
    Task<int> UpsertEpisodesAsync(Guid showId, List<PodcastEpisode> episodes);
    Task<List<PodcastEpisode>> GetEpisodesForShowAsync(Guid showId, int limit);
    Task AddSubscriptionAsync(PodcastSubscription subscription);
    Task RemoveSubscriptionAsync(PodcastSubscription subscription);
    Task<bool> ProfileHasOtherSubscriptionsAsync(Guid showId, Guid excludingProfileId);
    Task DeleteShowAsync(Guid showId);
    Task<List<PodcastShow>> GetShowsDueForRefreshAsync(DateTime threshold);

    Task<PodcastEpisode?> GetEpisodeByIdAsync(Guid episodeId);
    Task<bool> ProfileSubscribesToShowAsync(Guid profileId, Guid showId);
    Task<PodcastEpisodeProfileState?> GetStateAsync(Guid profileId, Guid episodeId);
    Task UpsertStateAsync(Guid profileId, Guid episodeId, double positionSeconds, bool isPlayed);
    Task<Dictionary<Guid, PodcastEpisodeProfileState>> GetStatesForShowAsync(Guid profileId, Guid showId);
    Task<List<PodcastEpisode>> GetEpisodesAcrossShowsAsync(IEnumerable<Guid> showIds, DateTime? since, int limit);
    Task<Dictionary<Guid, PodcastEpisodeProfileState>> GetStatesForEpisodesAsync(Guid profileId, IEnumerable<Guid> episodeIds);
    Task<List<PodcastShow>> GetCatalogShowsAsync();
    Task<bool> IsShowInCatalogAsync(string feedUrl);
}
