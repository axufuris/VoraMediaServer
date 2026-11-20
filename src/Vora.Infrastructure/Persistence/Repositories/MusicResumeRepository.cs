using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MusicResumeRepository(VoraDbContext context) : IMusicResumeRepository
{
    public async Task SaveAsync(Guid profileId, MusicPlaySource source, Guid sourceId, IReadOnlyList<Guid> trackIds, int trackIndex)
    {
        try
        {
            await UpsertAsync(profileId, source, sourceId, trackIds, trackIndex);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            await UpsertAsync(profileId, source, sourceId, trackIds, trackIndex);
        }

        var stale = await context.MusicResumePoints
            .Where(p => p.ProfileId == profileId)
            .OrderByDescending(p => p.UpdatedAt)
            .Skip(MusicResumeLimits.MaxPoints)
            .ToListAsync();
        if (stale.Count == 0) return;

        context.MusicResumePoints.RemoveRange(stale);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid profileId, MusicPlaySource source, Guid sourceId)
    {
        var point = await context.MusicResumePoints
            .FirstOrDefaultAsync(p => p.ProfileId == profileId && p.Source == source && p.SourceId == sourceId);
        if (point == null) return;

        context.MusicResumePoints.Remove(point);
        await context.SaveChangesAsync();
    }

    public Task<List<MusicResumePoint>> GetAsync(Guid profileId, int limit) =>
        context.MusicResumePoints
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .OrderByDescending(p => p.UpdatedAt)
            .Take(limit)
            .ToListAsync();

    public Task<MusicResumePoint?> GetAsync(Guid profileId, MusicPlaySource source, Guid sourceId) =>
        context.MusicResumePoints
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProfileId == profileId && p.Source == source && p.SourceId == sourceId);

    private async Task UpsertAsync(Guid profileId, MusicPlaySource source, Guid sourceId, IReadOnlyList<Guid> trackIds, int trackIndex)
    {
        var point = await context.MusicResumePoints
            .FirstOrDefaultAsync(p => p.ProfileId == profileId && p.Source == source && p.SourceId == sourceId);
        if (point == null)
        {
            point = new MusicResumePoint { ProfileId = profileId, Source = source, SourceId = sourceId };
            context.MusicResumePoints.Add(point);
        }

        point.TrackIds = trackIds.ToList();
        point.TrackIndex = trackIndex;
        point.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
