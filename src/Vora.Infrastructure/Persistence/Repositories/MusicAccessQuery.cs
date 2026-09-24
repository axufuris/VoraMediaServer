using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

// The one definition of which tracks a profile may see. It used to be written
// out in each repository, and the copies drifted into three different rules:
// MusicRepository checked both the unrated block and the Clean/Explicit
// allowlist; seven queries in MusicRecommendationRepository checked only the
// unrated block, so a profile allowed only Clean music was handed Explicit tracks
// in radio, stations, mixes and genre pages; and one more applied the unrated
// block only when the allowlist was also restricted. Parental controls are only
// as strong as their weakest copy, so there is one copy.
internal static class MusicAccessQuery
{
    public static IQueryable<Track> ApplyMusicRatings(this IQueryable<Track> query, MusicAccessFilter access)
    {
        // Independent of the allowlist. "Block unrated" means an untagged track
        // is out whether or not the profile has an allowlist.
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        // An untagged track passes the allowlist, because the allowlist says
        // which ratings are permitted, not that a track must carry one — that is
        // what BlockUnratedContent is for.
        if (!access.HasAllRatings)
        {
            var allowed = access.AllowedRatings;
            query = query.Where(t => t.ContentRating == null || allowed.Contains(t.ContentRating));
        }

        return query;
    }

    public static IQueryable<Track> ApplyMusicAccess(this IQueryable<Track> query, MusicAccessFilter access)
    {
        if (!access.HasAllLibraryAccess)
        {
            var allowedLibraries = access.AllowedLibraryIds;
            query = query.Where(t => allowedLibraries.Contains(t.LibraryId));
        }

        return query.ApplyMusicRatings(access);
    }
}
