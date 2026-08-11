using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Collections;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MediaDedupeRepository : IMediaDedupeRepository
{
    private readonly VoraDbContext _context;

    public MediaDedupeRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<TvShowMergeResultVM> MergeDuplicateTvShowsAsync(Guid? libraryId)
    {
        var result = new TvShowMergeResultVM();

        var shows = await _context.Set<TvShow>()
            .Where(t => libraryId == null || t.LibraryId == libraryId)
            .Select(t => new { t.Id, t.LibraryId, t.TmdbId, t.ImdbId, t.AddedAt })
            .ToListAsync();

        var groups = shows
            .Select(s => new
            {
                s.Id,
                s.LibraryId,
                s.AddedAt,
                Key = !string.IsNullOrEmpty(s.TmdbId) ? "tmdb:" + s.TmdbId
                    : !string.IsNullOrEmpty(s.ImdbId) ? "imdb:" + s.ImdbId
                    : null
            })
            .Where(s => s.Key != null)
            .GroupBy(s => new { s.LibraryId, s.Key })
            .Where(g => g.Count() > 1)
            .ToList();

        if (groups.Count == 0) return result;

        var groupShowIds = groups.SelectMany(g => g.Select(x => x.Id)).ToList();
        var episodeCounts = (await _context.Set<Episode>()
                .Where(e => groupShowIds.Contains(e.Season.TvShowId))
                .GroupBy(e => e.Season.TvShowId)
                .Select(g => new { ShowId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.ShowId, x => x.Count);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(s => episodeCounts.TryGetValue(s.Id, out var c) ? c : 0)
                .ThenBy(s => s.AddedAt)
                .ToList();

            var keeperId = ordered[0].Id;
            foreach (var drop in ordered.Skip(1))
            {
                result.PartsMoved += await MergeShowIntoAsync(keeperId, drop.Id, result.AffectedEpisodeIds);
                result.ShowsRemoved++;
            }
            result.GroupsMerged++;
        }

        return result;
    }

    private async Task<int> MergeShowIntoAsync(Guid keeperId, Guid dropId, List<Guid> affectedEpisodeIds)
    {
        var keeperSeasons = await _context.Set<Season>()
            .Where(s => s.TvShowId == keeperId)
            .Include(s => s.Episodes)
            .ToListAsync();

        var dropSeasons = await _context.Set<Season>()
            .Where(s => s.TvShowId == dropId)
            .Include(s => s.Episodes).ThenInclude(e => e.MediaParts)
            .ToListAsync();

        var keeperSeasonByNumber = keeperSeasons
            .GroupBy(s => s.SeasonNumber)
            .ToDictionary(g => g.Key, g => g.First());

        var partsMoved = 0;

        foreach (var dropSeason in dropSeasons)
        {
            if (!keeperSeasonByNumber.TryGetValue(dropSeason.SeasonNumber, out var keeperSeason))
            {
                dropSeason.TvShowId = keeperId;
                continue;
            }

            var keeperEpisodeByNumber = keeperSeason.Episodes
                .GroupBy(e => e.EpisodeNumber)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var dropEpisode in dropSeason.Episodes.ToList())
            {
                if (keeperEpisodeByNumber.TryGetValue(dropEpisode.EpisodeNumber, out var keeperEpisode))
                {
                    foreach (var part in dropEpisode.MediaParts.ToList())
                    {
                        dropEpisode.MediaParts.Remove(part);
                        keeperEpisode.MediaParts.Add(part);
                        part.MediaItemId = keeperEpisode.Id;
                        partsMoved++;
                    }

                    await MergeUserDataAsync(dropEpisode.Id, keeperEpisode.Id);
                    await MergeCollectionMembershipsAsync(dropEpisode.Id, keeperEpisode.Id);
                    affectedEpisodeIds.Add(keeperEpisode.Id);

                    dropSeason.Episodes.Remove(dropEpisode);
                    _context.Remove(dropEpisode);
                }
                else
                {
                    dropSeason.Episodes.Remove(dropEpisode);
                    dropEpisode.SeasonId = keeperSeason.Id;
                    keeperSeason.Episodes.Add(dropEpisode);
                }
            }

            await MergeUserDataAsync(dropSeason.Id, keeperSeason.Id);
            await MergeCollectionMembershipsAsync(dropSeason.Id, keeperSeason.Id);
            _context.Remove(dropSeason);
        }

        await MergeUserDataAsync(dropId, keeperId);
        await MergeCollectionMembershipsAsync(dropId, keeperId);

        var dropShow = await _context.Set<TvShow>().FirstAsync(t => t.Id == dropId);
        _context.Remove(dropShow);

        await _context.SaveChangesAsync();
        return partsMoved;
    }

    private async Task MergeUserDataAsync(Guid fromItemId, Guid toItemId)
    {
        var fromStates = await _context.Set<UserMediaState>().Where(u => u.MediaItemId == fromItemId).ToListAsync();
        if (fromStates.Count > 0)
        {
            var toStates = (await _context.Set<UserMediaState>().Where(u => u.MediaItemId == toItemId).ToListAsync())
                .GroupBy(u => u.ProfileId).ToDictionary(g => g.Key, g => g.First());

            foreach (var from in fromStates)
            {
                if (toStates.TryGetValue(from.ProfileId, out var to))
                {
                    to.IsPlayed = to.IsPlayed || from.IsPlayed;
                    to.ResumePositionSeconds = Math.Max(to.ResumePositionSeconds, from.ResumePositionSeconds);
                    to.LastPlayedAt = to.LastPlayedAt >= from.LastPlayedAt ? to.LastPlayedAt : from.LastPlayedAt;
                    to.IsHiddenFromContinueWatching = to.IsHiddenFromContinueWatching && from.IsHiddenFromContinueWatching;
                    _context.Remove(from);
                }
                else
                {
                    from.MediaItemId = toItemId;
                    toStates[from.ProfileId] = from;
                }
            }
        }

        var fromRatings = await _context.Set<UserMediaRating>().Where(u => u.MediaItemId == fromItemId).ToListAsync();
        if (fromRatings.Count > 0)
        {
            var toRatings = (await _context.Set<UserMediaRating>().Where(u => u.MediaItemId == toItemId).ToListAsync())
                .GroupBy(u => u.ProfileId).ToDictionary(g => g.Key, g => g.First());

            foreach (var from in fromRatings)
            {
                if (toRatings.TryGetValue(from.ProfileId, out var to))
                {
                    to.Rating = Math.Max(to.Rating, from.Rating);
                    to.RatedAt = to.RatedAt >= from.RatedAt ? to.RatedAt : from.RatedAt;
                    _context.Remove(from);
                }
                else
                {
                    from.MediaItemId = toItemId;
                    toRatings[from.ProfileId] = from;
                }
            }
        }
    }

    private async Task MergeCollectionMembershipsAsync(Guid fromItemId, Guid toItemId)
    {
        var fromMemberships = await _context.Set<CollectionItem>()
            .Where(ci => ci.MediaItemId == fromItemId)
            .ToListAsync();
        if (fromMemberships.Count == 0)
        {
            return;
        }

        var keeperCollections = (await _context.Set<CollectionItem>()
            .Where(ci => ci.MediaItemId == toItemId)
            .Select(ci => ci.CollectionId)
            .ToListAsync()).ToHashSet();

        foreach (var membership in fromMemberships)
        {
            _context.Remove(membership);

            if (keeperCollections.Add(membership.CollectionId))
            {
                _context.Add(new CollectionItem
                {
                    CollectionId = membership.CollectionId,
                    MediaItemId = toItemId,
                    SortOrder = membership.SortOrder,
                    InUniverseYear = membership.InUniverseYear,
                    AddedAt = membership.AddedAt
                });
            }
        }
    }

    public async Task<List<MediaItem>> GetMediaItemsWithMultiplePartsAsync()
    {
        var movies = await _context.MediaItems.OfType<Movie>()
            .Include(m => m.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(m => m.MediaParts).ThenInclude(p => p.AudioTracks)
            .Where(m => m.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        var episodes = await _context.MediaItems.OfType<Episode>()
            .Include(e => e.Season).ThenInclude(s => s.TvShow)
            .Include(e => e.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(e => e.MediaParts).ThenInclude(p => p.AudioTracks)
            .Where(e => e.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        var tracks = await _context.MediaItems.OfType<Track>()
            .Include(t => t.Album).ThenInclude(a => a!.Artist)
            .Include(t => t.MediaParts)
            .Where(t => t.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        return movies.Cast<MediaItem>()
            .Concat(episodes)
            .Concat(tracks)
            .ToList();
    }

    public async Task<MediaPart?> GetMediaPartByIdAsync(Guid partId)
    {
        return await _context.MediaParts.FirstOrDefaultAsync(p => p.Id == partId);
    }

    public async Task DeleteMediaPartAsync(MediaPart part)
    {
        _context.MediaParts.Remove(part);
        await _context.SaveChangesAsync();
    }

    public async Task<MediaDedupeSettings?> GetGlobalSettingsAsync()
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryId == null);
    }

    public async Task<MediaDedupeSettings?> GetLibraryOverrideAsync(Guid libraryId)
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryId == libraryId);
    }

    public async Task<List<MediaDedupeSettings>> GetAllLibraryOverridesAsync()
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .Where(s => s.LibraryId != null)
            .ToListAsync();
    }

    public async Task<MediaDedupeSettings> UpsertSettingsAsync(MediaDedupeSettings settings)
    {
        var existing = await _context.MediaDedupeSettings
            .FirstOrDefaultAsync(s => s.LibraryId == settings.LibraryId);

        settings.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            _context.MediaDedupeSettings.Add(settings);
        }
        else
        {
            settings.Id = existing.Id;
            _context.Entry(existing).CurrentValues.SetValues(settings);
        }

        await _context.SaveChangesAsync();
        return settings;
    }

    public async Task DeleteLibraryOverrideAsync(Guid libraryId)
    {
        var existing = await _context.MediaDedupeSettings
            .FirstOrDefaultAsync(s => s.LibraryId == libraryId);
        if (existing == null) return;

        _context.MediaDedupeSettings.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<List<MediaDedupeIgnoredGroup>> GetIgnoredGroupsAsync()
    {
        var groups = await _context.MediaDedupeIgnoredGroups
            .Include(g => g.MediaItem)
            .AsNoTracking()
            .OrderByDescending(g => g.IgnoredAt)
            .ToListAsync();

        var episodeIds = groups
            .Where(g => g.MediaItem is Episode)
            .Select(g => g.MediaItemId)
            .ToList();

        if (episodeIds.Count > 0)
        {
            var episodes = await _context.MediaItems.OfType<Episode>()
                .Include(e => e.Season).ThenInclude(s => s.TvShow)
                .AsNoTracking()
                .Where(e => episodeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            foreach (var group in groups)
            {
                if (group.MediaItem is Episode && episodes.TryGetValue(group.MediaItemId, out var hydrated))
                {
                    group.MediaItem = hydrated;
                }
            }
        }

        var trackIds = groups
            .Where(g => g.MediaItem is Track)
            .Select(g => g.MediaItemId)
            .ToList();

        if (trackIds.Count > 0)
        {
            var tracks = await _context.MediaItems.OfType<Track>()
                .Include(t => t.Album).ThenInclude(a => a!.Artist)
                .AsNoTracking()
                .Where(t => trackIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id);

            foreach (var group in groups)
            {
                if (group.MediaItem is Track && tracks.TryGetValue(group.MediaItemId, out var hydrated))
                {
                    group.MediaItem = hydrated;
                }
            }
        }

        return groups;
    }

    public async Task<MediaDedupeIgnoredGroup?> GetIgnoredGroupAsync(Guid mediaItemId, string resolution)
    {
        return await _context.MediaDedupeIgnoredGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.MediaItemId == mediaItemId && g.Resolution == resolution);
    }

    public async Task AddIgnoredGroupAsync(MediaDedupeIgnoredGroup group)
    {
        _context.MediaDedupeIgnoredGroups.Add(group);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveIgnoredGroupAsync(Guid ignoredGroupId)
    {
        var existing = await _context.MediaDedupeIgnoredGroups
            .FirstOrDefaultAsync(g => g.Id == ignoredGroupId);
        if (existing == null) return;

        _context.MediaDedupeIgnoredGroups.Remove(existing);
        await _context.SaveChangesAsync();
    }
}
