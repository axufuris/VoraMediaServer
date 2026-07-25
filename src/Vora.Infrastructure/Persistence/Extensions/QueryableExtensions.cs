using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Extensions;

public static class QueryableExtensions
{
    public static IQueryable<MediaItem> ApplyAccessFilters(
        this IQueryable<MediaItem> query,
        bool hasAllLibs, List<Guid> allowedLibs,
        bool hasAllRatings, List<string> allowedMovieRatings, List<string> allowedTvRatings, bool blockUnrated)
    {
        query = query.Where(m => m.MissingSince == null);

        if (!hasAllLibs)
        {
            query = query.Where(m => allowedLibs.Contains(m.LibraryId));
        }

        if (!hasAllRatings)
        {
            query = query.Where(m =>
                (m is Movie && (m.ContentRating != null ? allowedMovieRatings.Contains(m.ContentRating ?? string.Empty) : !blockUnrated)) ||
                (m is TvShow && (m.ContentRating != null ? allowedTvRatings.Contains(m.ContentRating ?? string.Empty) : !blockUnrated)) ||
                (m is Season && (((Season)m).ContentRating != null ? allowedTvRatings.Contains(((Season)m).ContentRating ?? string.Empty) :
                                ((Season)m).TvShow.ContentRating != null ? allowedTvRatings.Contains(((Season)m).TvShow.ContentRating ?? string.Empty) : !blockUnrated)) ||
                (m is Episode && (((Episode)m).ContentRating != null ? allowedTvRatings.Contains(((Episode)m).ContentRating ?? string.Empty) :
                                 ((Episode)m).Season.ContentRating != null ? allowedTvRatings.Contains(((Episode)m).Season.ContentRating ?? string.Empty) :
                                 ((Episode)m).Season.TvShow.ContentRating != null ? allowedTvRatings.Contains(((Episode)m).Season.TvShow.ContentRating ?? string.Empty) : !blockUnrated)) ||
                (m is Track)
            );
        }

        return query;
    }
}