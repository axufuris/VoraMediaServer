using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Application.Media;

public static class MusicResumeLimits
{
    public const int MaxPoints = 20;
    public const int MaxQueue = 300;
    public const int MaxAccepted = 1000;
    public const int LookBehind = 50;
}

public interface IMusicResumeRepository
{
    Task SaveAsync(Guid profileId, MusicPlaySource source, Guid sourceId, IReadOnlyList<Guid> trackIds, int trackIndex);
    Task RemoveAsync(Guid profileId, MusicPlaySource source, Guid sourceId);
    Task<List<MusicResumePoint>> GetAsync(Guid profileId, int limit);
    Task<MusicResumePoint?> GetAsync(Guid profileId, MusicPlaySource source, Guid sourceId);
}
