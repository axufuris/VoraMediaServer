using Microsoft.EntityFrameworkCore;
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

    public async Task<UpNextResultVM> GetUpNextAsync(Guid mediaId, string? contextType, Guid? contextId, Guid? profileId)
    {
        var result = new UpNextResultVM();

        var currentMedia = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == mediaId)
            .Select(m => new
            {
                m.Id,
                Type = m is Movie ? "Movie" : m is TvShow ? "TvShow" : m is Episode ? "Episode" : "Unknown",
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
                .Select(p => p.Order)
                .FirstOrDefaultAsync();

            if (currentPlaylistItem > 0)
            {
                result.NextItem = await _context.PlaylistItems
                    .AsNoTracking()
                    .Where(p => p.PlaylistId == contextId.Value && p.Order > currentPlaylistItem && p.MediaItem.MissingSince == null)
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
                    .Where(p => p.PlaylistId == contextId.Value && p.Order < currentPlaylistItem && p.MediaItem.MissingSince == null)
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
                result.NextItem = await _context.Set<Episode>()
                    .AsNoTracking()
                    .Where(e => e.Season.TvShowId == seasonInfo.TvShowId && e.MissingSince == null &&
                               ((e.Season.SeasonNumber == seasonInfo.SeasonNumber && e.EpisodeNumber > currentMedia.EpisodeNumber) ||
                                 e.Season.SeasonNumber > seasonInfo.SeasonNumber))
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
                                 e.Season.SeasonNumber < seasonInfo.SeasonNumber))
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
            UpNextItemVM? nextEpisode = null;

            if (profileId.HasValue)
            {
                nextEpisode = await _context.Set<Episode>()
                    .AsNoTracking()
                    .Where(e => e.Season.TvShowId == mediaId && e.MissingSince == null)
                    .Where(e => !_context.UserMediaStates.Any(s => s.ProfileId == profileId.Value && s.MediaItemId == e.Id && s.IsPlayed))
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
            }

            nextEpisode ??= await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => e.Season.TvShowId == mediaId && e.MissingSince == null)
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

            result.NextItem = nextEpisode;
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
                    .OrderByDescending(m => m.ReleaseDate)
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

    public async Task<List<ContinueWatchingVM>> GetContinueWatchingAsync(Guid profileId, int limit = 15)
    {
        var hiddenItemIds = await _context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsHiddenFromContinueWatching)
            .Select(s => s.MediaItemId)
            .ToListAsync();
        var hiddenSet = hiddenItemIds.ToHashSet();

        var movieRows = await _context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.ResumePositionSeconds > 0 && !s.IsPlayed && !s.IsHiddenFromContinueWatching && s.MediaItem is Movie && s.MediaItem.MissingSince == null)
            .Select(s => new
            {
                Vm = new ContinueWatchingVM
                {
                    Id = s.MediaItem.Id,
                    Title = s.MediaItem.Title,
                    SortTitle = s.MediaItem.SortTitle,
                    Overview = s.MediaItem.Overview,
                    Type = "Movie",
                    PosterUrl = s.MediaItem.PosterUrl,
                    BackgroundUrl = s.MediaItem.BackgroundUrl,
                    ReleaseDate = s.MediaItem.ReleaseDate,
                    ContentRating = s.MediaItem.ContentRating,
                    Genres = s.MediaItem.Genres.Select(g => g.Name).ToList(),
                    ResumePositionSeconds = s.ResumePositionSeconds,
                    DurationSeconds = s.MediaItem.Analysis != null && s.MediaItem.Analysis.Duration.HasValue ? (double?)s.MediaItem.Analysis.Duration.Value.TotalSeconds : null,
                },
                LastPlayedAt = s.LastPlayedAt,
            })
            .ToListAsync();

        var watchedShowSummaries = await _context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.MediaItem is Episode && !s.IsHiddenFromContinueWatching && s.MediaItem.MissingSince == null)
            .Select(s => new
            {
                TvShowId = ((Episode)s.MediaItem).Season.TvShowId,
                LastPlayed = s.LastPlayedAt,
            })
            .GroupBy(x => x.TvShowId)
            .Select(g => new { TvShowId = g.Key, LastPlayed = g.Max(x => x.LastPlayed) })
            .ToListAsync();

        var candidateShowIds = watchedShowSummaries
            .Where(s => !hiddenSet.Contains(s.TvShowId))
            .Select(s => s.TvShowId)
            .ToList();

        var showRows = new List<(ContinueWatchingVM Vm, DateTime LastPlayedAt)>();

        if (candidateShowIds.Count > 0)
        {
            var unplayedKeys = await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => candidateShowIds.Contains(e.Season.TvShowId))
                .Where(e => e.MissingSince == null)
                .Where(e => !_context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.Id && (s.IsPlayed || s.IsHiddenFromContinueWatching)))
                .Select(e => new { e.Id, TvShowId = e.Season.TvShowId, SeasonNumber = e.Season.SeasonNumber, e.EpisodeNumber })
                .ToListAsync();

            var nextEpisodeIds = unplayedKeys
                .GroupBy(e => e.TvShowId)
                .Select(g => g.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).First().Id)
                .ToList();

            var candidateEpisodes = await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => nextEpisodeIds.Contains(e.Id))
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    EpisodePosterUrl = e.PosterUrl,
                    EpisodeBackdrop = e.BackgroundUrl,
                    e.ReleaseDate,
                    SeasonNumber = e.Season.SeasonNumber,
                    e.EpisodeNumber,
                    TvShowId = e.Season.TvShowId,
                    TvShowTitle = e.Season.TvShow.Title,
                    ShowOverview = e.Season.TvShow.Overview,
                    ShowPosterUrl = e.Season.TvShow.PosterUrl,
                    ShowBackgroundUrl = e.Season.TvShow.BackgroundUrl,
                    ShowContentRating = e.Season.TvShow.ContentRating,
                    ShowGenres = e.Season.TvShow.Genres.Select(g => g.Name).ToList(),
                    DurationSeconds = e.Analysis != null && e.Analysis.Duration.HasValue ? (double?)e.Analysis.Duration.Value.TotalSeconds : null
                })
                .ToListAsync();

            var candidateEpisodeIds = candidateEpisodes.Select(e => e.Id).ToList();
            var resumePositions = await _context.UserMediaStates
                .AsNoTracking()
                .Where(s => s.ProfileId == profileId && candidateEpisodeIds.Contains(s.MediaItemId))
                .Select(s => new { s.MediaItemId, s.ResumePositionSeconds })
                .ToDictionaryAsync(s => s.MediaItemId, s => (double?)s.ResumePositionSeconds);

            var firstUnplayedEpisodes = candidateEpisodes
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.EpisodePosterUrl,
                    e.EpisodeBackdrop,
                    e.ReleaseDate,
                    e.SeasonNumber,
                    e.EpisodeNumber,
                    e.TvShowId,
                    e.TvShowTitle,
                    e.ShowOverview,
                    e.ShowPosterUrl,
                    e.ShowBackgroundUrl,
                    e.ShowContentRating,
                    e.ShowGenres,
                    e.DurationSeconds,
                    ResumePositionSeconds = resumePositions.TryGetValue(e.Id, out var r) ? r : null
                })
                .ToList();

            var earliestPerShow = firstUnplayedEpisodes
                .GroupBy(e => e.TvShowId)
                .Select(g => g.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).First())
                .ToList();

            var lastPlayedByShow = watchedShowSummaries.ToDictionary(s => s.TvShowId, s => s.LastPlayed);

            foreach (var ep in earliestPerShow)
            {
                showRows.Add((
                    new ContinueWatchingVM
                    {
                        Id = ep.Id,
                        Title = ep.Title,
                        Overview = ep.ShowOverview,
                        Type = "Episode",
                        PosterUrl = ep.ShowPosterUrl ?? ep.EpisodePosterUrl,
                        BackgroundUrl = ep.ShowBackgroundUrl ?? ep.EpisodeBackdrop,
                        ReleaseDate = ep.ReleaseDate,
                        ContentRating = ep.ShowContentRating,
                        Genres = ep.ShowGenres,
                        TvShowId = ep.TvShowId,
                        TvShowTitle = ep.TvShowTitle,
                        SeasonNumber = ep.SeasonNumber,
                        EpisodeNumber = ep.EpisodeNumber,
                        ResumePositionSeconds = ep.ResumePositionSeconds ?? 0,
                        DurationSeconds = ep.DurationSeconds,
                    },
                    lastPlayedByShow.TryGetValue(ep.TvShowId, out var lp) ? lp : DateTime.MinValue
                ));
            }
        }

        return movieRows
            .Select(m => (Vm: m.Vm, LastPlayedAt: m.LastPlayedAt))
            .Concat(showRows)
            .OrderByDescending(r => r.LastPlayedAt)
            .Take(limit)
            .Select(r => r.Vm)
            .ToList();
    }

    public async Task AttachUserMediaStateAsync(MediaDetailsVM vm, Guid profileId)
    {
        var rating = await _context.Set<UserMediaRating>()
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && r.MediaItemId == vm.Id)
            .Select(r => (decimal?)r.Rating)
            .FirstOrDefaultAsync();
        vm.MyRating = rating;

        if (vm.Type == "Movie" || vm.Type == "Episode")
        {
            var state = await _context.Set<UserMediaState>()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.MediaItemId == vm.Id);

            if (state != null)
            {
                vm.IsPlayed = state.IsPlayed;
                vm.ResumePositionSeconds = state.ResumePositionSeconds;
            }
        }
        else if (vm.Type == "TvShow")
        {
            var episodeMappings = await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => e.Season.TvShowId == vm.Id)
                .Select(e => new { e.Id, e.SeasonId })
                .ToListAsync();

            if (episodeMappings.Count > 0)
            {
                var allEpisodeIds = episodeMappings.Select(e => e.Id).ToList();

                var playedEpisodeIds = await _context.Set<UserMediaState>()
                    .AsNoTracking()
                    .Where(s => s.ProfileId == profileId && s.IsPlayed && allEpisodeIds.Contains(s.MediaItemId))
                    .Select(s => s.MediaItemId)
                    .ToListAsync();

                var playedSet = playedEpisodeIds.ToHashSet();

                var statsBySeason = episodeMappings
                    .GroupBy(x => x.SeasonId)
                    .ToDictionary(g => g.Key, g => new
                    {
                        TotalEpisodes = g.Count(),
                        PlayedEpisodes = g.Count(x => playedSet.Contains(x.Id))
                    });

                foreach (var season in vm.Seasons)
                {
                    if (statsBySeason.TryGetValue(season.Id, out var stats))
                    {
                        season.UnplayedItemCount = stats.TotalEpisodes - stats.PlayedEpisodes;
                        season.IsPlayed = stats.TotalEpisodes > 0 && stats.TotalEpisodes == stats.PlayedEpisodes;
                    }
                }

                int totalUnplayed = statsBySeason.Values.Sum(x => x.TotalEpisodes - x.PlayedEpisodes);
                int totalEpisodes = statsBySeason.Values.Sum(x => x.TotalEpisodes);

                vm.UnplayedItemCount = totalUnplayed;
                vm.IsPlayed = totalEpisodes > 0 && totalUnplayed == 0;
            }

            var seasonIds = vm.Seasons.Select(s => s.Id).ToList();
            if (seasonIds.Count > 0)
            {
                var seasonRatings = await _context.Set<UserMediaRating>()
                    .AsNoTracking()
                    .Where(r => r.ProfileId == profileId && seasonIds.Contains(r.MediaItemId))
                    .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);
                foreach (var s in vm.Seasons)
                {
                    if (seasonRatings.TryGetValue(s.Id, out var seasonRating)) s.MyRating = seasonRating;
                }
            }
        }
        else if (vm.Type == "Season")
        {
            var episodeIds = vm.Episodes.Select(e => e.Id).ToList();

            var states = await _context.Set<UserMediaState>()
                .AsNoTracking()
                .Where(s => s.ProfileId == profileId && episodeIds.Contains(s.MediaItemId))
                .ToDictionaryAsync(s => s.MediaItemId);

            var episodeRatings = await _context.Set<UserMediaRating>()
                .AsNoTracking()
                .Where(r => r.ProfileId == profileId && episodeIds.Contains(r.MediaItemId))
                .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);

            int unplayedTotal = 0;

            foreach (var ep in vm.Episodes)
            {
                if (states.TryGetValue(ep.Id, out var state))
                {
                    ep.IsPlayed = state.IsPlayed;
                    ep.ResumePositionSeconds = state.ResumePositionSeconds;
                }
                if (episodeRatings.TryGetValue(ep.Id, out var episodeRating))
                {
                    ep.MyRating = episodeRating;
                }
                if (!ep.IsPlayed) unplayedTotal++;
            }

            vm.UnplayedItemCount = unplayedTotal;
            vm.IsPlayed = unplayedTotal == 0 && vm.Episodes.Count > 0;
        }
    }

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

    public async Task AttachLibraryItemUserStatesAsync(IEnumerable<LibraryItemVM> items, Guid profileId)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        var itemIds = itemList.Select(i => i.Id).ToList();

        var directStates = await _context.Set<UserMediaState>()
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && itemIds.Contains(s.MediaItemId))
            .ToDictionaryAsync(s => s.MediaItemId, s => s.IsPlayed);

        var ratings = await _context.Set<UserMediaRating>()
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && itemIds.Contains(r.MediaItemId))
            .ToDictionaryAsync(r => r.MediaItemId, r => r.Rating);

        var tvShowIds = itemList.Where(i => i.Type == "TvShow").Select(i => i.Id).ToList();
        var seasonIds = itemList.Where(i => i.Type == "Season").Select(i => i.Id).ToList();

        var tvStats = new Dictionary<Guid, (int total, int played)>();
        var seasonStats = new Dictionary<Guid, (int total, int played)>();

        if (tvShowIds.Count > 0 || seasonIds.Count > 0)
        {
            var episodeMappings = await _context.Set<Episode>()
                .AsNoTracking()
                .Where(e => tvShowIds.Contains(e.Season.TvShowId) || seasonIds.Contains(e.SeasonId))
                .Select(e => new { e.Id, e.SeasonId, TvShowId = e.Season.TvShowId })
                .ToListAsync();

            if (episodeMappings.Count > 0)
            {
                var allEpisodeIds = episodeMappings.Select(e => e.Id).ToList();

                var playedEpisodeIds = await _context.Set<UserMediaState>()
                    .AsNoTracking()
                    .Where(s => s.ProfileId == profileId && s.IsPlayed && allEpisodeIds.Contains(s.MediaItemId))
                    .Select(s => s.MediaItemId)
                    .ToListAsync();

                var playedSet = playedEpisodeIds.ToHashSet();

                if (tvShowIds.Count > 0)
                {
                    tvStats = episodeMappings
                        .Where(e => tvShowIds.Contains(e.TvShowId))
                        .GroupBy(x => x.TvShowId)
                        .ToDictionary(g => g.Key, g => (total: g.Count(), played: g.Count(x => playedSet.Contains(x.Id))));
                }

                if (seasonIds.Count > 0)
                {
                    seasonStats = episodeMappings
                        .Where(e => seasonIds.Contains(e.SeasonId))
                        .GroupBy(x => x.SeasonId)
                        .ToDictionary(g => g.Key, g => (total: g.Count(), played: g.Count(x => playedSet.Contains(x.Id))));
                }
            }
        }

        foreach (var item in itemList)
        {
            if (ratings.TryGetValue(item.Id, out var rating)) item.MyRating = rating;

            if (item.Type == "Movie" || item.Type == "Episode")
            {
                if (directStates.TryGetValue(item.Id, out var played)) item.IsPlayed = played;
            }
            else if (item.Type == "TvShow")
            {
                if (tvStats.TryGetValue(item.Id, out var stats))
                {
                    item.UnplayedItemCount = stats.total - stats.played;
                    item.IsPlayed = stats.total > 0 && stats.total == stats.played;
                }
            }
            else if (item.Type == "Season")
            {
                if (seasonStats.TryGetValue(item.Id, out var stats))
                {
                    item.UnplayedItemCount = stats.total - stats.played;
                    item.IsPlayed = stats.total > 0 && stats.total == stats.played;
                }
            }
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
}
