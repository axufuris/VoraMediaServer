using Microsoft.EntityFrameworkCore;
using Vora.Application.Streaming;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

internal static class EpisodeToPlay
{
    public static async Task<Guid?> PickAsync(VoraDbContext context, IQueryable<Episode> episodes, Guid? profileId)
    {
        var present = episodes.AsNoTracking().Where(e => e.MissingSince == null);
        if (profileId is not Guid profile) return await FirstAsync(present);

        var order = await context.UserProfiles.AsNoTracking()
            .Where(p => p.Id == profile)
            .Select(p => p.UpNextOrder)
            .FirstOrDefaultAsync();

        if (order == UpNextOrder.WhereILeftOff && await WhereLeftOffAsync(context, present, profile) is Guid leftOff)
        {
            return leftOff;
        }

        return await FirstAsync(present.Where(e => !context.UserMediaStates.Any(s => s.ProfileId == profile && s.MediaItemId == e.Id && s.IsPlayed)))
            ?? await FirstAsync(present);
    }

    private static async Task<Guid?> WhereLeftOffAsync(VoraDbContext context, IQueryable<Episode> present, Guid profile)
    {
        var last = await present
            .SelectMany(
                e => context.UserMediaStates.Where(s => s.ProfileId == profile && s.MediaItemId == e.Id
                    && (s.IsPlayed || s.ResumePositionSeconds >= WatchStateTransition.MinimumResumeSeconds)),
                (e, s) => new { e.Id, e.Season.SeasonNumber, e.EpisodeNumber, s.IsPlayed, s.LastPlayedAt })
            .OrderByDescending(x => x.LastPlayedAt)
            .FirstOrDefaultAsync();
        if (last == null) return null;
        if (!last.IsPlayed) return last.Id;

        return await FirstAsync(present.Where(e => e.Season.SeasonNumber > last.SeasonNumber
            || (e.Season.SeasonNumber == last.SeasonNumber && e.EpisodeNumber > last.EpisodeNumber)));
    }

    private static Task<Guid?> FirstAsync(IQueryable<Episode> episodes) =>
        episodes
            .OrderBy(e => e.Season.SeasonNumber).ThenBy(e => e.EpisodeNumber)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync();
}
