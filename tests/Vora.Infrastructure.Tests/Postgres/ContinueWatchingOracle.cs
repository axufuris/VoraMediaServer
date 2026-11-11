using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Streaming;
using Vora.Application.Sync.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;

namespace Vora.Infrastructure.Tests.Postgres;

/// <summary>
/// Continue Watching as it was worked out before ContinueWatchingQuery: one
/// EpisodeToPlay pick per show. Kept as the reference the single query must
/// agree with (PostgresContinueWatchingTests), not for use.
/// </summary>
internal sealed class ContinueWatchingOracle(VoraDbContext context)
{
    private async Task<List<Guid>> WhereEachShowWasLeftAsync(List<Guid> showIds, HashSet<Guid> showsWithRegularEpisodes, HashSet<Guid> unwatched, Guid profileId)
    {
        var picks = new List<Guid>();
        foreach (var showId in showIds)
        {
            var regular = showsWithRegularEpisodes.Contains(showId);
            var episodes = context.Set<Episode>().Where(e => e.Season.TvShowId == showId && (!regular || e.Season.SeasonNumber != EpisodeSequence.SpecialsSeason));
            if (await EpisodeToPlay.PickAsync(context, episodes, profileId) is Guid pick && unwatched.Contains(pick)) picks.Add(pick);
        }
        return picks;
    }

    public async Task<List<ContinueWatchingVM>> GetContinueWatchingAsync(Guid profileId, int limit)
    {
        var hiddenItemIds = await context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsHiddenFromContinueWatching)
            .Select(s => s.MediaItemId)
            .ToListAsync();
        var hiddenSet = hiddenItemIds.ToHashSet();

        var movieRows = await context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.ResumePositionSeconds >= WatchStateTransition.MinimumResumeSeconds && !s.IsPlayed && !s.IsHiddenFromContinueWatching && s.MediaItem is Movie && s.MediaItem.MissingSince == null)
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

        var watchedShowSummaries = await context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.MediaItem is Episode && !s.IsHiddenFromContinueWatching && s.MediaItem.MissingSince == null)
            .Where(s => s.IsPlayed || s.ResumePositionSeconds >= WatchStateTransition.MinimumResumeSeconds)
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
            var unplayedKeys = await context.Set<Episode>()
                .AsNoTracking()
                .Where(e => candidateShowIds.Contains(e.Season.TvShowId))
                .Where(e => e.MissingSince == null)
                .Where(e => !context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.Id && (s.IsPlayed || s.IsHiddenFromContinueWatching)))
                .Select(e => new
                {
                    e.Id,
                    TvShowId = e.Season.TvShowId,
                    SeasonNumber = e.Season.SeasonNumber,
                    e.EpisodeNumber,
                    InProgress = context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.Id && s.ResumePositionSeconds >= WatchStateTransition.MinimumResumeSeconds)
                })
                .ToListAsync();

            var showsWithRegularEpisodes = (await context.Set<Episode>()
                .AsNoTracking()
                .Where(e => candidateShowIds.Contains(e.Season.TvShowId) && e.MissingSince == null && e.Season.SeasonNumber != EpisodeSequence.SpecialsSeason)
                .Select(e => e.Season.TvShowId)
                .Distinct()
                .ToListAsync()).ToHashSet();

            var upNextOrder = await context.UserProfiles.AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => p.UpNextOrder)
                .FirstOrDefaultAsync();
            var nextEpisodeIds = upNextOrder == UpNextOrder.WhereILeftOff
                ? await WhereEachShowWasLeftAsync(candidateShowIds, showsWithRegularEpisodes, unplayedKeys.Select(e => e.Id).ToHashSet(), profileId)
                : unplayedKeys
                    .GroupBy(e => e.TvShowId)
                    .Select(g => EpisodeSequence.PickNextUp(g, e => e.SeasonNumber, e => e.EpisodeNumber, e => e.InProgress, showsWithRegularEpisodes.Contains(g.Key))?.Id)
                    .OfType<Guid>()
                    .ToList();

            var candidateEpisodes = await context.Set<Episode>()
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
                    e.SeasonId,
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
            var resumePositions = await context.UserMediaStates
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
                    e.SeasonId,
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
                        SeasonId = ep.SeasonId,
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
}
