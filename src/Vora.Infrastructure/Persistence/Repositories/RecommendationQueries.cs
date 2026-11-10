using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Recommendations;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

// What both For You providers know about a profile's viewing, so the local rows
// and the AI rows agree on what has been seen.
internal static class RecommendationQueries
{
    // Not yet started by the profile: never played, not part-way through and not
    // rated, since a rating says it was seen somewhere. A show is started as soon
    // as any of its episodes is; its own row is rarely marked played, which is
    // how shows watched to the end kept being recommended as unwatched.
    public static Expression<Func<MediaItem, bool>> Untouched(VoraDbContext context, Guid profileId) =>
        m => !context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == m.Id && (s.IsPlayed || s.ResumePositionSeconds > 0))
            && !context.Set<Episode>().Any(e => e.Season.TvShowId == m.Id
                && context.UserMediaStates.Any(s => s.ProfileId == profileId && s.MediaItemId == e.Id && (s.IsPlayed || s.ResumePositionSeconds > 0)))
            && !context.UserMediaRatings.Any(r => r.ProfileId == profileId && r.MediaItemId == m.Id);

    // The movies and shows the profile has watched or rated, in the library when
    // one is given. A show counts once, last watched when its latest episode was.
    public static async Task<List<WatchedTitle>> WatchedTitlesAsync(VoraDbContext context, Guid profileId, Guid? libraryId)
    {
        var played = await context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && s.IsPlayed)
            .Where(MediaCapabilities.IsBrowsableTitle.On((Vora.Domain.Entities.Users.UserMediaState s) => s.MediaItem))
            .Select(s => new { s.MediaItemId, s.LastPlayedAt })
            .ToListAsync();

        var showsByEpisode = await (
                from s in context.UserMediaStates.AsNoTracking()
                join e in context.Set<Episode>() on s.MediaItemId equals e.Id
                where s.ProfileId == profileId && s.IsPlayed
                group s by e.Season.TvShowId into g
                select new { ShowId = g.Key, LastPlayedAt = g.Max(s => s.LastPlayedAt) })
            .ToListAsync();

        var rated = await context.UserMediaRatings
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId)
            .Where(MediaCapabilities.IsBrowsableTitle.On((Vora.Domain.Entities.Users.UserMediaRating r) => r.MediaItem))
            .Select(r => new { r.MediaItemId, r.Rating, r.RatedAt })
            .ToListAsync();

        var lastWatched = new Dictionary<Guid, DateTime>();
        foreach (var (id, at) in played.Select(p => (p.MediaItemId, p.LastPlayedAt)).Concat(showsByEpisode.Select(s => (s.ShowId, s.LastPlayedAt))))
        {
            if (!lastWatched.TryGetValue(id, out var known) || at > known) lastWatched[id] = at;
        }
        var ratings = rated.GroupBy(r => r.MediaItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.RatedAt).First());

        var ids = lastWatched.Keys.Concat(ratings.Keys).Distinct().ToList();
        if (ids.Count == 0) return new List<WatchedTitle>();

        var titles = context.MediaItems
            .AsNoTracking()
            .Where(MediaCapabilities.IsBrowsableTitle)
            .Where(m => ids.Contains(m.Id) && m.MissingSince == null);
        if (libraryId.HasValue) titles = titles.Where(m => m.LibraryId == libraryId.Value);

        var found = await titles
            .Select(m => new { m.Id, m.Title, Year = m.ReleaseDate.HasValue ? m.ReleaseDate.Value.Year : (int?)null })
            .ToListAsync();

        return found.Select(m => new WatchedTitle(
                m.Id,
                m.Title,
                m.Year,
                lastWatched.TryGetValue(m.Id, out var at) ? at : null,
                ratings.TryGetValue(m.Id, out var r) ? r.Rating : null,
                ratings.TryGetValue(m.Id, out var rr) ? rr.RatedAt : null))
            .ToList();
    }

    // "movies", "TV shows", or both for the For You page across libraries.
    public static async Task<string> TitleKindAsync(VoraDbContext context, Guid? libraryId)
    {
        if (!libraryId.HasValue) return "movies and TV shows";
        var type = await context.Set<MediaLibrary>().AsNoTracking()
            .Where(l => l.Id == libraryId.Value)
            .Select(l => (LibraryType?)l.Type)
            .FirstOrDefaultAsync();
        return type == LibraryType.TvShow ? "TV shows" : type == LibraryType.Movie ? "movies" : "movies and TV shows";
    }
}
