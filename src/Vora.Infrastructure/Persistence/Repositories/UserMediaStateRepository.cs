using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vora.Application.Libraries.ViewModels;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Application.Sync.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class UserMediaStateRepository : IUserMediaStateRepository
{
    private readonly VoraDbContext _context;

    public UserMediaStateRepository(VoraDbContext context)
    {
        _context = context;
    }

    private static readonly Expression<Func<Episode, UpNextItemVM>> AsUpNextItem = e => new UpNextItemVM
    {
        Id = e.Id,
        Title = e.Title,
        TvShowTitle = e.Season.TvShow.Title,
        SeasonNumber = e.Season.SeasonNumber,
        EpisodeNumber = e.EpisodeNumber,
        Type = "Episode",
        PosterUrl = e.PosterUrl,
        BackgroundUrl = e.BackgroundUrl,
        Overview = e.Overview
    };

    private async Task<UpNextItemVM?> FirstEpisodeToPlayAsync(IQueryable<Episode> episodes, Guid? profileId)
    {
        if (await EpisodeToPlay.PickAsync(_context, episodes, profileId) is not Guid episodeId) return null;
        return await _context.Set<Episode>().AsNoTracking()
            .Where(e => e.Id == episodeId)
            .Select(AsUpNextItem)
            .FirstOrDefaultAsync();
    }

    public async Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId, Guid? profileId)
    {
        var result = new UpNextResultVM();

        var currentMedia = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaId)
            .Select(m => new
            {
                m.Id,
                Type = m is Movie ? "Movie" : m is TvShow ? "TvShow" : m is Season ? "Season" : m is Episode ? "Episode" : "Unknown",
                DirectorId = m.Cast.Where(c => (c.Roles & MediaCastRole.Director) != 0).Select(c => c.ActorId).FirstOrDefault(),
                GenreIds = m.Genres.Select(g => g.Id).ToList(),
                SeasonId = m is Episode ? ((Episode)m).SeasonId : (Guid?)null,
                EpisodeNumber = m is Episode ? ((Episode)m).EpisodeNumber : (int?)null
            })
            .FirstOrDefaultAsync();

        if (currentMedia == null) return result;

        if (contextType == "playlist" && contextId.HasValue)
        {
            var currentPlaylistItem = await _context.PlaylistItems
                .AsNoTracking()
                .Where(p => p.PlaylistId == contextId.Value && p.MediaItemId == mediaId)
                .Select(p => (int?)p.Order)
                .FirstOrDefaultAsync();

            if (currentPlaylistItem.HasValue)
            {
                var currentOrder = currentPlaylistItem.Value;

                result.NextItem = await _context.PlaylistItems
                    .AsNoTracking()
                    .Where(p => p.PlaylistId == contextId.Value && p.Order > currentOrder && p.MediaItem.MissingSince == null)
                    .OrderBy(p => p.Order)
                    .Select(p => new UpNextItemVM
                    {
                        Id = p.MediaItem.Id,
                        Title = p.MediaItem.Title,
                        TvShowTitle = p.MediaItem is Episode ? ((Episode)p.MediaItem).Season.TvShow.Title : null,
                        SeasonNumber = p.MediaItem is Episode ? (int?)((Episode)p.MediaItem).Season.SeasonNumber : null,
                        EpisodeNumber = p.MediaItem is Episode ? (int?)((Episode)p.MediaItem).EpisodeNumber : null,
                        Type = p.MediaItem is Movie ? "Movie" : p.MediaItem is Episode ? "Episode" : "Unknown",
                        PosterUrl = p.MediaItem.PosterUrl,
                        BackgroundUrl = p.MediaItem.BackgroundUrl,
                        Overview = p.MediaItem.Overview
                    })
                    .FirstOrDefaultAsync();

                result.PreviousItem = await _context.PlaylistItems
                    .AsNoTracking()
                    .Where(p => p.PlaylistId == contextId.Value && p.Order < currentOrder && p.MediaItem.MissingSince == null)
                    .OrderByDescending(p => p.Order)
                    .Select(p => new UpNextItemVM
                    {
                        Id = p.MediaItem.Id,
                        Title = p.MediaItem.Title,
                        TvShowTitle = p.MediaItem is Episode ? ((Episode)p.MediaItem).Season.TvShow.Title : null,
                        SeasonNumber = p.MediaItem is Episode ? (int?)((Episode)p.MediaItem).Season.SeasonNumber : null,
                        EpisodeNumber = p.MediaItem is Episode ? (int?)((Episode)p.MediaItem).EpisodeNumber : null,
                        Type = p.MediaItem is Movie ? "Movie" : p.MediaItem is Episode ? "Episode" : "Unknown",
                        PosterUrl = p.MediaItem.PosterUrl,
                        BackgroundUrl = p.MediaItem.BackgroundUrl,
                        Overview = p.MediaItem.Overview
                    })
                    .FirstOrDefaultAsync();
            }
        }
        else if (currentMedia.Type == "Episode" && currentMedia.SeasonId.HasValue)
        {
            var seasonInfo = await _context.Set<Season>()
                .AsNoTracking()
                .Where(s => s.Id == currentMedia.SeasonId.Value)
                .Select(s => new { s.TvShowId, s.SeasonNumber })
                .FirstOrDefaultAsync();

            if (seasonInfo != null)
            {
                var inSpecials = EpisodeSequence.IsSpecial(seasonInfo.SeasonNumber);

                result.NextItem = await _context.Set<Episode>()
                    .AsNoTracking()
                    .Where(e => e.Season.TvShowId == seasonInfo.TvShowId && e.MissingSince == null &&
                               ((e.Season.SeasonNumber == seasonInfo.SeasonNumber && e.EpisodeNumber > currentMedia.EpisodeNumber) ||
                                 (!inSpecials && e.Season.SeasonNumber > seasonInfo.SeasonNumber)))
                    .OrderBy(e => e.Season.SeasonNumber).ThenBy(e => e.EpisodeNumber)
                    .Select(e => new UpNextItemVM
                    {
                        Id = e.Id,
                        Title = e.Title,
                        TvShowTitle = e.Season.TvShow.Title,
                        SeasonNumber = e.Season.SeasonNumber,
                        EpisodeNumber = e.EpisodeNumber,
                        Type = "Episode",
                        PosterUrl = e.PosterUrl,
                        BackgroundUrl = e.BackgroundUrl,
                        Overview = e.Overview
                    })
                    .FirstOrDefaultAsync();

                result.PreviousItem = await _context.Set<Episode>()
                    .AsNoTracking()
                    .Where(e => e.Season.TvShowId == seasonInfo.TvShowId &&
                               ((e.Season.SeasonNumber == seasonInfo.SeasonNumber && e.EpisodeNumber < currentMedia.EpisodeNumber) ||
                                 (e.Season.SeasonNumber < seasonInfo.SeasonNumber && e.Season.SeasonNumber != EpisodeSequence.SpecialsSeason)))
                    .OrderByDescending(e => e.Season.SeasonNumber).ThenByDescending(e => e.EpisodeNumber)
                    .Select(e => new UpNextItemVM
                    {
                        Id = e.Id,
                        Title = e.Title,
                        TvShowTitle = e.Season.TvShow.Title,
                        SeasonNumber = e.Season.SeasonNumber,
                        EpisodeNumber = e.EpisodeNumber,
                        Type = "Episode",
                        PosterUrl = e.PosterUrl,
                        BackgroundUrl = e.BackgroundUrl,
                        Overview = e.Overview
                    })
                    .FirstOrDefaultAsync();
            }
        }
        else if (currentMedia.Type == "TvShow")
        {
            var hasRegularEpisodes = await _context.Set<Episode>()
                .AnyAsync(e => e.Season.TvShowId == mediaId && e.MissingSince == null && e.Season.SeasonNumber != EpisodeSequence.SpecialsSeason);

            result.NextItem = await FirstEpisodeToPlayAsync(
                _context.Set<Episode>().Where(e => e.Season.TvShowId == mediaId && (!hasRegularEpisodes || e.Season.SeasonNumber != EpisodeSequence.SpecialsSeason)),
                profileId);
        }
        else if (currentMedia.Type == "Season")
        {
            result.NextItem = await FirstEpisodeToPlayAsync(_context.Set<Episode>().Where(e => e.SeasonId == mediaId), profileId);
        }

        if (currentMedia.Type == "Movie")
        {
            if (currentMedia.DirectorId != Guid.Empty)
            {
                var directorMovies = await _context.MediaCastMembers
                    .AsNoTracking()
                    .Where(c => c.ActorId == currentMedia.DirectorId && c.MediaItemId != mediaId && c.MediaItem is Movie)
                    .Select(c => new UpNextItemVM
                    {
                        Id = c.MediaItem.Id,
                        Title = c.MediaItem.Title,
                        Type = "Movie",
                        PosterUrl = c.MediaItem.PosterUrl,
                        BackgroundUrl = c.MediaItem.BackgroundUrl,
                        Overview = c.MediaItem.Overview
                    })
                    .Take(10)
                    .ToListAsync();

                if (directorMovies.Count > 0)
                    result.RelatedLists.Add(new RelatedListVM { Title = "More from this Director", Items = directorMovies });
            }

            if (currentMedia.GenreIds.Count > 0)
            {
                var genreMovies = await _context.MediaItems
                    .AsNoTracking()
                    .Where(m => m.Id != mediaId && m is Movie && m.Genres.Any(g => currentMedia.GenreIds.Contains(g.Id)))
                    .OrderByDescending(m => m.ReleaseDate.HasValue)
                    .ThenByDescending(m => m.ReleaseDate)
                    .Select(m => new UpNextItemVM
                    {
                        Id = m.Id,
                        Title = m.Title,
                        Type = "Movie",
                        PosterUrl = m.PosterUrl,
                        BackgroundUrl = m.BackgroundUrl,
                        Overview = m.Overview
                    })
                    .Take(15)
                    .ToListAsync();

                if (genreMovies.Count > 0)
                    result.RelatedLists.Add(new RelatedListVM { Title = "Similar Movies", Items = genreMovies });
            }
        }

        return result;
    }

    public Task<List<ContinueWatchingVM>> GetContinueWatchingAsync(Guid profileId, int limit = 15) =>
        _context.Database
            .SqlQueryRaw<ContinueWatchingVM>(
                ContinueWatchingQuery.Sql,
                new NpgsqlParameter("profileId", profileId),
                new NpgsqlParameter("limit", limit))
            .ToListAsync();

    public async Task SetMediaPlayedStateAsync(Guid mediaItemId, Guid profileId, bool isPlayed)
    {
        var item = await _context.MediaItems.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return;

        List<Guid> idsToUpdate;
        if (item is TvShow)
        {
            idsToUpdate = await _context.Set<Episode>().AsNoTracking().Where(e => e.Season.TvShowId == mediaItemId).Select(e => e.Id).ToListAsync();
        }
        else if (item is Season)
        {
            idsToUpdate = await _context.Set<Episode>().AsNoTracking().Where(e => e.SeasonId == mediaItemId).Select(e => e.Id).ToListAsync();
        }
        else
        {
            idsToUpdate = new List<Guid> { mediaItemId };
        }

        if (idsToUpdate.Count == 0) return;

        await _context.UserMediaStates
            .Where(s => s.ProfileId == profileId && idsToUpdate.Contains(s.MediaItemId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsPlayed, isPlayed)
                .SetProperty(x => x.ResumePositionSeconds, 0));

        var existingIds = await _context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && idsToUpdate.Contains(s.MediaItemId))
            .Select(s => s.MediaItemId)
            .ToListAsync();

        var existingSet = existingIds.ToHashSet();
        var missing = idsToUpdate.Where(id => !existingSet.Contains(id)).ToList();

        if (missing.Count > 0)
        {
            await _context.UserMediaStates.AddRangeAsync(missing.Select(id => new UserMediaState
            {
                ProfileId = profileId,
                MediaItemId = id,
                IsPlayed = isPlayed,
                ResumePositionSeconds = 0
            }));
            await _context.SaveChangesAsync();
        }
    }

    public async Task HideFromContinueWatchingAsync(Guid profileId, Guid mediaItemId)
    {
        var rowsAffected = await _context.UserMediaStates
            .Where(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHiddenFromContinueWatching, true));

        if (rowsAffected == 0)
        {
            _context.UserMediaStates.Add(new UserMediaState
            {
                ProfileId = profileId,
                MediaItemId = mediaItemId,
                IsHiddenFromContinueWatching = true,
                LastPlayedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Dictionary<Guid, decimal>> GetMediaRatingsAsync(Guid profileId, IEnumerable<Guid> mediaItemIds)
    {
        var idList = mediaItemIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, decimal>();

        return await _context.Set<UserMediaRating>()
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && idList.Contains(r.MediaItemId))
            .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);
    }

    public async Task<SetMediaRatingResult> SetMediaRatingAsync(Guid profileId, Guid mediaItemId, decimal? rating, bool isAdmin)
    {
        var item = await _context.MediaItems.FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (item == null) return new SetMediaRatingResult { Found = false, ServerAdminRatingChanged = false };

        var existing = await _context.UserMediaRatings.FirstOrDefaultAsync(r => r.ProfileId == profileId && r.MediaItemId == mediaItemId);

        if (rating.HasValue)
        {
            if (existing == null)
            {
                _context.UserMediaRatings.Add(new UserMediaRating
                {
                    ProfileId = profileId,
                    MediaItemId = mediaItemId,
                    Rating = rating.Value,
                    RatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Rating = rating.Value;
                existing.RatedAt = DateTime.UtcNow;
            }
        }
        else if (existing != null)
        {
            _context.UserMediaRatings.Remove(existing);
        }

        bool serverAdminChanged = false;
        if (isAdmin && item.ServerAdminRating != rating)
        {
            item.ServerAdminRating = rating;
            serverAdminChanged = true;
        }

        await _context.SaveChangesAsync();
        return new SetMediaRatingResult { Found = true, ServerAdminRatingChanged = serverAdminChanged };
    }

    public async Task<ViewingHistoryVM?> GetViewingHistoryAsync(Guid profileId)
    {
        if (!await _context.UserProfiles.AnyAsync(p => p.Id == profileId)) return null;

        var states = _context.UserMediaStates.Where(s => s.ProfileId == profileId);
        return new ViewingHistoryVM
        {
            WatchedCount = await states.CountAsync(s => s.IsPlayed),
            InProgressCount = await states.CountAsync(s => !s.IsPlayed && s.ResumePositionSeconds > 0),
            RatingCount = await _context.UserMediaRatings.CountAsync(r => r.ProfileId == profileId)
        };
    }

    public async Task<ClearedViewingHistoryVM?> ClearViewingHistoryAsync(Guid profileId, bool watchHistory, bool ratings)
    {
        if (!await _context.UserProfiles.AnyAsync(p => p.Id == profileId)) return null;

        var states = watchHistory
            ? await _context.UserMediaStates.Where(s => s.ProfileId == profileId).ToListAsync()
            : new List<UserMediaState>();
        var profileRatings = ratings
            ? await _context.UserMediaRatings.Where(r => r.ProfileId == profileId).ToListAsync()
            : new List<UserMediaRating>();
        _context.UserMediaStates.RemoveRange(states);
        _context.UserMediaRatings.RemoveRange(profileRatings);

        // The stored For You rows were built from what is being cleared ("Because
        // you watched ..."), so they go too and are built again from what is left.
        if (watchHistory || ratings)
        {
            _context.RecommendationSnapshots.RemoveRange(
                await _context.RecommendationSnapshots.Where(s => s.ProfileId == profileId).ToListAsync());
        }

        foreach (var archive in await _context.PreservedUserMediaData.Where(p => p.ProfileId == profileId).ToListAsync())
        {
            if (watchHistory)
            {
                archive.HasState = false;
                archive.ResumePositionSeconds = 0;
                archive.IsPlayed = false;
                archive.IsHiddenFromContinueWatching = false;
                archive.LastPlayedAt = null;
            }
            if (ratings)
            {
                archive.Rating = null;
                archive.RatedAt = null;
            }
            if (!archive.HasState && archive.Rating is null) _context.PreservedUserMediaData.Remove(archive);
        }

        await _context.SaveChangesAsync();
        return new ClearedViewingHistoryVM { WatchStatesRemoved = states.Count, RatingsRemoved = profileRatings.Count };
    }
}
