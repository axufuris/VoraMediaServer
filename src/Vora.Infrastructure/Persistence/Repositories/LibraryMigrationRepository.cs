using Microsoft.EntityFrameworkCore;
using Vora.Application.LibraryMigration;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;

namespace Vora.Infrastructure.Persistence.Repositories;

public class LibraryMigrationRepository : ILibraryMigrationRepository
{
    private readonly VoraDbContext _context;

    public LibraryMigrationRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<MediaItemMatchRow>> FindMatchesAsync(IReadOnlyCollection<string> tmdbIds, IReadOnlyCollection<string> imdbIds, IReadOnlyCollection<string> tvdbIds)
    {
        if (tmdbIds.Count == 0 && imdbIds.Count == 0 && tvdbIds.Count == 0)
        {
            return new List<MediaItemMatchRow>();
        }

        var tmdbList = tmdbIds.ToList();
        var imdbList = imdbIds.ToList();
        var tvdbList = tvdbIds.ToList();

        return await _context.MediaItems
            .AsNoTracking()
            .Where(m => (m.TmdbId != null && tmdbList.Contains(m.TmdbId))
                     || (m.ImdbId != null && imdbList.Contains(m.ImdbId))
                     || (m.TvdbId != null && tvdbList.Contains(m.TvdbId)))
            .Select(m => new MediaItemMatchRow
            {
                Id = m.Id,
                TmdbId = m.TmdbId,
                ImdbId = m.ImdbId,
                TvdbId = m.TvdbId
            })
            .ToListAsync();
    }

    public async Task<List<EpisodeMatchRow>> FindEpisodeMatchesAsync(IReadOnlyCollection<string> showTmdbIds, IReadOnlyCollection<string> showImdbIds, IReadOnlyCollection<string> showTvdbIds)
    {
        if (showTmdbIds.Count == 0 && showImdbIds.Count == 0 && showTvdbIds.Count == 0)
        {
            return new List<EpisodeMatchRow>();
        }

        var tmdbList = showTmdbIds.ToList();
        var imdbList = showImdbIds.ToList();
        var tvdbList = showTvdbIds.ToList();

        return await _context.MediaItems
            .OfType<Episode>()
            .AsNoTracking()
            .Where(e => (e.Season.TvShow.TmdbId != null && tmdbList.Contains(e.Season.TvShow.TmdbId))
                     || (e.Season.TvShow.ImdbId != null && imdbList.Contains(e.Season.TvShow.ImdbId))
                     || (e.Season.TvShow.TvdbId != null && tvdbList.Contains(e.Season.TvShow.TvdbId)))
            .Select(e => new EpisodeMatchRow
            {
                Id = e.Id,
                ShowTmdbId = e.Season.TvShow.TmdbId,
                ShowImdbId = e.Season.TvShow.ImdbId,
                ShowTvdbId = e.Season.TvShow.TvdbId,
                SeasonNumber = e.Season.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                EndEpisodeNumber = e.EndEpisodeNumber
            })
            .ToListAsync();
    }

    public async Task BulkUpsertWatchStatesAsync(Guid profileId, IReadOnlyCollection<WatchStateUpsert> entries)
    {
        if (entries.Count == 0) return;

        var targetIds = entries.Select(e => e.MediaItemId).Distinct().ToList();
        var existing = await _context.UserMediaStates
            .Where(s => s.ProfileId == profileId && targetIds.Contains(s.MediaItemId))
            .ToListAsync();
        var byItem = existing.ToDictionary(s => s.MediaItemId);

        foreach (var entry in entries)
        {
            if (byItem.TryGetValue(entry.MediaItemId, out var state))
            {
                state.IsPlayed = entry.IsPlayed;
                state.ResumePositionSeconds = entry.ResumePositionSeconds;
                if (entry.LastPlayedAt.HasValue)
                {
                    state.LastPlayedAt = entry.LastPlayedAt.Value;
                }
            }
            else
            {
                _context.UserMediaStates.Add(new UserMediaState
                {
                    ProfileId = profileId,
                    MediaItemId = entry.MediaItemId,
                    IsPlayed = entry.IsPlayed,
                    ResumePositionSeconds = entry.ResumePositionSeconds,
                    LastPlayedAt = entry.LastPlayedAt ?? DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task BulkSetAdminRatingsAsync(IReadOnlyCollection<RatingUpsert> entries)
    {
        if (entries.Count == 0) return;

        var byItem = entries
            .GroupBy(e => e.MediaItemId)
            .ToDictionary(g => g.Key, g => g.First().Rating);

        var targetIds = byItem.Keys.ToList();
        var items = await _context.MediaItems
            .Where(m => targetIds.Contains(m.Id))
            .ToListAsync();

        foreach (var item in items)
        {
            if (byItem.TryGetValue(item.Id, out var rating))
            {
                item.ServerAdminRating = rating;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task BulkUpsertRatingsAsync(Guid profileId, IReadOnlyCollection<RatingUpsert> entries)
    {
        if (entries.Count == 0) return;

        var targetIds = entries.Select(e => e.MediaItemId).Distinct().ToList();
        var existing = await _context.UserMediaRatings
            .Where(r => r.ProfileId == profileId && targetIds.Contains(r.MediaItemId))
            .ToListAsync();
        var byItem = existing.ToDictionary(r => r.MediaItemId);

        foreach (var entry in entries)
        {
            if (byItem.TryGetValue(entry.MediaItemId, out var rating))
            {
                rating.Rating = entry.Rating;
                if (entry.RatedAt.HasValue)
                {
                    rating.RatedAt = entry.RatedAt.Value;
                }
            }
            else
            {
                _context.UserMediaRatings.Add(new UserMediaRating
                {
                    ProfileId = profileId,
                    MediaItemId = entry.MediaItemId,
                    Rating = entry.Rating,
                    RatedAt = entry.RatedAt ?? DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
    }
}
