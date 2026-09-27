using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Extensions;

public static class QueryableExtensions
{
    // Films follow the film allowlist, TV follows the TV allowlist, and an empty
    // allowlist means that kind is not restricted. Nothing about music decides
    // what video a profile sees; the only setting the kinds share is blocking
    // unrated content. This used to hinge on one cross-media flag that was true
    // only when films, TV AND music were all open, so limiting a child's music
    // to Clean hid every rated film and show, and limiting films hid all of TV.
    public static IQueryable<MediaItem> ApplyAccessFilters(
        this IQueryable<MediaItem> query,
        bool hasAllLibs, List<Guid> allowedLibs,
        List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated)
    {
        query = query.Where(m => m.MissingSince == null);

        if (!hasAllLibs)
        {
            query = query.Where(m => allowedLibs.Contains(m.LibraryId));
        }

        var restrictMovies = allowedMovieRatings.Count > 0;
        var restrictTv = allowedTvRatings.Count > 0;
        if (!restrictMovies && !restrictTv && !blockUnrated)
        {
            return query;
        }

        return query.Where(m =>
            (m is Movie && (m.ContentRating != null ? !restrictMovies || allowedMovieRatings.Contains(m.ContentRating ?? string.Empty) : !blockUnrated)) ||
            (m is TvShow && (m.ContentRating != null ? !restrictTv || allowedTvRatings.Contains(m.ContentRating ?? string.Empty) : !blockUnrated)) ||
            (m is Season && (((Season)m).ContentRating != null ? !restrictTv || allowedTvRatings.Contains(((Season)m).ContentRating ?? string.Empty) :
                            ((Season)m).TvShow.ContentRating != null ? !restrictTv || allowedTvRatings.Contains(((Season)m).TvShow.ContentRating ?? string.Empty) : !blockUnrated)) ||
            (m is Episode && (((Episode)m).ContentRating != null ? !restrictTv || allowedTvRatings.Contains(((Episode)m).ContentRating ?? string.Empty) :
                             ((Episode)m).Season.ContentRating != null ? !restrictTv || allowedTvRatings.Contains(((Episode)m).Season.ContentRating ?? string.Empty) :
                             ((Episode)m).Season.TvShow.ContentRating != null ? !restrictTv || allowedTvRatings.Contains(((Episode)m).Season.TvShow.ContentRating ?? string.Empty) : !blockUnrated)) ||
            (m is Track)
        );
    }
}
