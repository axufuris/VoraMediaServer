using System.Linq.Expressions;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Libraries.ViewModels;

public static class TitleWatchState
{
    public static Expression<Func<MediaItem, bool>> IsPlayed(Guid? profileId, IQueryable<UserMediaState> states) =>
        item => profileId != null && (item is TvShow
            ? ((TvShow)item).Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MissingSince == null)
                && ((TvShow)item).Seasons.SelectMany(season => season.Episodes).All(episode => episode.MissingSince != null
                    || states.Any(state => state.ProfileId == profileId && state.IsPlayed && state.MediaItemId == episode.Id))
            : item is Season
                ? ((Season)item).Episodes.Any(episode => episode.MissingSince == null)
                    && ((Season)item).Episodes.All(episode => episode.MissingSince != null
                        || states.Any(state => state.ProfileId == profileId && state.IsPlayed && state.MediaItemId == episode.Id))
                : (item is Movie || item is Episode)
                    && states.Any(state => state.ProfileId == profileId && state.IsPlayed && state.MediaItemId == item.Id));

    public static Expression<Func<MediaItem, int?>> EpisodesLeft(Guid? profileId, IQueryable<UserMediaState> states) =>
        item => profileId == null
            ? (int?)null
            : item is TvShow
                ? ((TvShow)item).Seasons.SelectMany(season => season.Episodes).Any(episode => episode.MissingSince == null)
                    ? ((TvShow)item).Seasons.SelectMany(season => season.Episodes).Count(episode => episode.MissingSince == null
                        && !states.Any(state => state.ProfileId == profileId && state.IsPlayed && state.MediaItemId == episode.Id))
                    : (int?)null
                : item is Season
                    ? ((Season)item).Episodes.Any(episode => episode.MissingSince == null)
                        ? ((Season)item).Episodes.Count(episode => episode.MissingSince == null
                            && !states.Any(state => state.ProfileId == profileId && state.IsPlayed && state.MediaItemId == episode.Id))
                        : (int?)null
                    : (int?)null;

    public static Expression<Func<MediaItem, decimal?>> Rating(Guid? profileId, IQueryable<UserMediaRating> ratings) =>
        item => profileId == null
            ? (decimal?)null
            : ratings.Where(rating => rating.ProfileId == profileId && rating.MediaItemId == item.Id)
                .Select(rating => (decimal?)rating.Rating)
                .FirstOrDefault();
}
