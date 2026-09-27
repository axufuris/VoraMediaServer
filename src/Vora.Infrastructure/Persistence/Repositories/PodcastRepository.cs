using Microsoft.EntityFrameworkCore;
using Vora.Application.Podcasts;
using Vora.Domain.Entities.Podcasts;

namespace Vora.Infrastructure.Persistence.Repositories;

public class PodcastRepository : IPodcastRepository
{
    private readonly VoraDbContext _context;

    public PodcastRepository(VoraDbContext context)
    {
        _context = context;
    }

    public Task<List<PodcastSubscription>> GetSubscriptionsForProfileAsync(Guid profileId) =>
        _context.PodcastSubscriptions
            .AsNoTracking()
            .Include(s => s.Show)
            .Where(s => s.ProfileId == profileId)
            .OrderByDescending(s => s.SubscribedAt)
            .ToListAsync();

    public Task<PodcastSubscription?> GetSubscriptionByIdAsync(Guid subscriptionId) =>
        _context.PodcastSubscriptions
            .Include(s => s.Show)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);

    public Task<PodcastShow?> GetShowByFeedUrlAsync(string feedUrl) =>
        _context.PodcastShows.FirstOrDefaultAsync(s => s.FeedUrl == feedUrl);

    public Task<PodcastShow?> GetShowByIdAsync(Guid showId) =>
        _context.PodcastShows.FirstOrDefaultAsync(s => s.Id == showId);

    public async Task<Dictionary<Guid, int>> GetEpisodeCountsAsync(IEnumerable<Guid> showIds)
    {
        var ids = showIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, int>();

        return await _context.PodcastEpisodes
            .AsNoTracking()
            .Where(e => ids.Contains(e.PodcastShowId))
            .GroupBy(e => e.PodcastShowId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task AddShowAsync(PodcastShow show)
    {
        await _context.PodcastShows.AddAsync(show);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateShowAsync(PodcastShow show)
    {
        _context.PodcastShows.Update(show);
        await _context.SaveChangesAsync();
    }

    public async Task<int> UpsertEpisodesAsync(Guid showId, List<PodcastEpisode> episodes)
    {
        if (episodes.Count == 0) return 0;

        var incomingGuids = episodes.Select(e => e.ExternalGuid).ToHashSet(StringComparer.Ordinal);
        var existing = await _context.PodcastEpisodes
            .Where(e => e.PodcastShowId == showId && incomingGuids.Contains(e.ExternalGuid))
            .ToListAsync();

        var existingByGuid = existing.ToDictionary(e => e.ExternalGuid, StringComparer.Ordinal);
        var seenInBatch = new HashSet<string>(StringComparer.Ordinal);
        var newCount = 0;

        foreach (var ep in episodes)
        {
            if (!seenInBatch.Add(ep.ExternalGuid)) continue;

            if (existingByGuid.TryGetValue(ep.ExternalGuid, out var current))
            {
                current.Title = ep.Title;
                current.Description = ep.Description;
                current.AudioUrl = ep.AudioUrl;
                current.ArtworkUrl = ep.ArtworkUrl;
                current.DurationSeconds = ep.DurationSeconds;
                current.PublishedAt = ep.PublishedAt;
                current.EpisodeNumber = ep.EpisodeNumber;
                current.SeasonNumber = ep.SeasonNumber;
            }
            else
            {
                ep.PodcastShowId = showId;
                await _context.PodcastEpisodes.AddAsync(ep);
                newCount++;
            }
        }

        await _context.SaveChangesAsync();
        return newCount;
    }

    public Task<List<PodcastEpisode>> GetEpisodesForShowAsync(Guid showId, int limit) =>
        _context.PodcastEpisodes
            .AsNoTracking()
            .Where(e => e.PodcastShowId == showId)
            .OrderByDescending(e => e.PublishedAt)
            .Take(limit)
            .ToListAsync();

    public async Task AddSubscriptionAsync(PodcastSubscription subscription)
    {
        await _context.PodcastSubscriptions.AddAsync(subscription);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveSubscriptionAsync(PodcastSubscription subscription)
    {
        _context.PodcastSubscriptions.Remove(subscription);
        await _context.SaveChangesAsync();
    }

    public Task<bool> ProfileHasOtherSubscriptionsAsync(Guid showId, Guid excludingProfileId) =>
        _context.PodcastSubscriptions
            .AnyAsync(s => s.PodcastShowId == showId && s.ProfileId != excludingProfileId);

    public async Task DeleteShowAsync(Guid showId)
    {
        var show = await _context.PodcastShows.FindAsync(showId);
        if (show == null) return;
        _context.PodcastShows.Remove(show);
        await _context.SaveChangesAsync();
    }

    public Task<List<PodcastShow>> GetShowsDueForRefreshAsync(DateTime threshold) =>
        _context.PodcastShows
            .Where(s => s.LastRefreshedAt == null || s.LastRefreshedAt < threshold)
            .OrderBy(s => s.LastRefreshedAt)
            .ToListAsync();

    public Task<PodcastEpisode?> GetEpisodeByIdAsync(Guid episodeId) =>
        _context.PodcastEpisodes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == episodeId);

    public Task<bool> ProfileSubscribesToShowAsync(Guid profileId, Guid showId) =>
        _context.PodcastSubscriptions
            .AnyAsync(s => s.ProfileId == profileId && s.PodcastShowId == showId);

    public Task<PodcastEpisodeProfileState?> GetStateAsync(Guid profileId, Guid episodeId) =>
        _context.PodcastEpisodeProfileStates
            .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.PodcastEpisodeId == episodeId);

    public async Task UpsertStateAsync(Guid profileId, Guid episodeId, double positionSeconds, bool isPlayed)
    {
        var existing = await _context.PodcastEpisodeProfileStates
            .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.PodcastEpisodeId == episodeId);

        if (existing == null)
        {
            await _context.PodcastEpisodeProfileStates.AddAsync(new PodcastEpisodeProfileState
            {
                ProfileId = profileId,
                PodcastEpisodeId = episodeId,
                PositionSeconds = positionSeconds,
                IsPlayed = isPlayed,
                LastListenedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.PositionSeconds = positionSeconds;
            existing.IsPlayed = isPlayed;
            existing.LastListenedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<Dictionary<Guid, PodcastEpisodeProfileState>> GetStatesForShowAsync(Guid profileId, Guid showId)
    {
        var episodeIds = await _context.PodcastEpisodes
            .AsNoTracking()
            .Where(e => e.PodcastShowId == showId)
            .Select(e => e.Id)
            .ToListAsync();

        if (episodeIds.Count == 0) return new Dictionary<Guid, PodcastEpisodeProfileState>();

        var states = await _context.PodcastEpisodeProfileStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && episodeIds.Contains(s.PodcastEpisodeId))
            .ToListAsync();

        return states.ToDictionary(s => s.PodcastEpisodeId);
    }

    public async Task<List<PodcastEpisode>> GetEpisodesAcrossShowsAsync(IEnumerable<Guid> showIds, DateTime? since, int limit)
    {
        var ids = showIds.ToList();
        if (ids.Count == 0) return new List<PodcastEpisode>();

        var query = _context.PodcastEpisodes
            .AsNoTracking()
            .Where(e => ids.Contains(e.PodcastShowId));

        if (since.HasValue)
        {
            var cutoff = since.Value;
            query = query.Where(e => e.PublishedAt != null && e.PublishedAt >= cutoff);
        }

        return await query
            .OrderByDescending(e => e.PublishedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Dictionary<Guid, PodcastEpisodeProfileState>> GetStatesForEpisodesAsync(Guid profileId, IEnumerable<Guid> episodeIds)
    {
        var ids = episodeIds.ToList();
        if (ids.Count == 0) return new Dictionary<Guid, PodcastEpisodeProfileState>();

        var states = await _context.PodcastEpisodeProfileStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && ids.Contains(s.PodcastEpisodeId))
            .ToListAsync();

        return states.ToDictionary(s => s.PodcastEpisodeId);
    }

    public Task<List<PodcastShow>> GetCatalogShowsAsync() =>
        _context.PodcastShows
            .AsNoTracking()
            .Where(s => s.IsInCatalog)
            .OrderBy(s => s.Title)
            .ToListAsync();

    public Task<bool> IsShowInCatalogAsync(string feedUrl) =>
        _context.PodcastShows
            .AnyAsync(s => s.FeedUrl == feedUrl && s.IsInCatalog);
}
