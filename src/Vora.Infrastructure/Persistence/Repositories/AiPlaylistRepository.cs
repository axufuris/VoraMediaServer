using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Media.Ai;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class AiPlaylistRepository : IAiPlaylistRepository
{
    private static readonly GeneratedMixKind[] AiKinds =
    {
        GeneratedMixKind.AiPlaylist, GeneratedMixKind.Bridge, GeneratedMixKind.Blend, GeneratedMixKind.Requested
    };

    private readonly VoraDbContext _context;

    public AiPlaylistRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<PlayedVector>> GetPlayedVectorsAsync(Guid profileId, int withinDays, int limit)
    {
        var since = DateTime.UtcNow.AddDays(-withinDays);
        var plays = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.PlayedAt >= since)
            .GroupBy(p => p.TrackId)
            .Select(g => new { TrackId = g.Key, Plays = g.Count() });

        var rows = await (from play in plays
                          join e in _context.MediaItemEmbeddings.AsNoTracking() on play.TrackId equals e.MediaItemId
                          where e.Embedding != null
                          orderby play.Plays descending
                          select new { e.Embedding, play.Plays })
            .Take(Math.Max(1, limit))
            .ToListAsync();

        return rows.Select(r => new PlayedVector(r.Embedding!.ToArray(), r.Plays)).ToList();
    }

    public async Task<List<AiTrackCandidate>> FindNearestTracksAsync(float[] vector, MusicAccessFilter access, AiTrackFilter filter, int limit)
    {
        var target = new Pgvector.Vector(vector);
        var exclude = filter.Exclude?.ToList() ?? new List<Guid>();

        var tracks = _context.Tracks.AsNoTracking().ApplyMusicAccess(access);
        if (filter.YearFrom is int from) tracks = tracks.Where(t => t.Album != null && t.Album.Year >= from);
        if (filter.YearTo is int to) tracks = tracks.Where(t => t.Album != null && t.Album.Year <= to);
        if (exclude.Count > 0) tracks = tracks.Where(t => !exclude.Contains(t.Id));

        var rows = await (from e in _context.MediaItemEmbeddings.AsNoTracking()
                          join t in tracks on e.MediaItemId equals t.Id
                          where e.Embedding != null
                          orderby e.Embedding!.CosineDistance(target)
                          select new
                          {
                              t.Id,
                              Artist = t.Artist ?? (t.Album != null ? t.Album.Artist.Name : null),
                              Art = t.Album != null ? t.Album.ArtworkUrl : null
                          })
            .Take(Math.Max(1, limit))
            .ToListAsync();

        return rows.Select(r => new AiTrackCandidate(r.Id, r.Artist ?? string.Empty, r.Art)).ToList();
    }

    // Opted in, with enough listening to have a taste, and no weekly set newer
    // than the cutoff. Never-generated profiles come first.
    public Task<List<Guid>> GetProfilesDueForWeeklyAsync(DateTime generatedBefore, int minPlays, int withinDays)
    {
        var since = DateTime.UtcNow.AddDays(-withinDays);
        return _context.UserProfiles
            .AsNoTracking()
            .Where(p => p.AiMusicPlaylistsEnabled)
            .Where(p => _context.TrackPlayHistory.Count(h => h.ProfileId == p.Id && h.PlayedAt >= since) >= minPlays)
            .Where(p => !_context.GeneratedMixes.Any(m => m.ProfileId == p.Id
                && (m.Kind == GeneratedMixKind.AiPlaylist || m.Kind == GeneratedMixKind.Bridge)
                && m.GeneratedAt >= generatedBefore))
            .Select(p => p.Id)
            .ToListAsync();
    }

    // Every other profile on the server that takes part in AI playlists.
    public Task<List<BlendPartner>> GetBlendPartnersAsync(Guid profileId) =>
        _context.UserProfiles
            .AsNoTracking()
            .Where(p => p.Id != profileId && p.AiMusicPlaylistsEnabled)
            .OrderBy(p => p.Name)
            .Select(p => new BlendPartner(p.Id, p.Name, p.ProfileImageUrl))
            .ToListAsync();

    public Task<int> CountRequestsSinceAsync(Guid profileId, DateTime since) =>
        _context.GeneratedMixes.CountAsync(m => m.ProfileId == profileId && m.Kind == GeneratedMixKind.Requested && m.GeneratedAt >= since);

    public Task<List<GeneratedMix>> GetAiMixesAsync(Guid profileId) =>
        _context.GeneratedMixes
            .AsNoTracking()
            .Where(m => m.ProfileId == profileId && AiKinds.Contains(m.Kind))
            .ToListAsync();

    public async Task ReplaceWeeklyAsync(Guid profileId, IReadOnlyList<GeneratedMix> mixes)
    {
        var old = await _context.GeneratedMixes
            .Where(m => m.ProfileId == profileId && (m.Kind == GeneratedMixKind.AiPlaylist || m.Kind == GeneratedMixKind.Bridge))
            .ToListAsync();
        _context.GeneratedMixes.RemoveRange(old);
        _context.GeneratedMixes.AddRange(mixes);
        await _context.SaveChangesAsync();
    }

    // One Blend per partner: making it again replaces it.
    public async Task ReplaceBlendAsync(GeneratedMix blend)
    {
        var old = await _context.GeneratedMixes
            .Where(m => m.ProfileId == blend.ProfileId && m.Kind == GeneratedMixKind.Blend && m.PartnerProfileId == blend.PartnerProfileId)
            .ToListAsync();
        _context.GeneratedMixes.RemoveRange(old);
        _context.GeneratedMixes.Add(blend);
        await _context.SaveChangesAsync();
    }

    // Keeps the newest few; the rest are dropped rather than piling up.
    public async Task AddRequestAsync(GeneratedMix request, int keep)
    {
        _context.GeneratedMixes.Add(request);
        await _context.SaveChangesAsync();

        var stale = await _context.GeneratedMixes
            .Where(m => m.ProfileId == request.ProfileId && m.Kind == GeneratedMixKind.Requested)
            .OrderByDescending(m => m.GeneratedAt)
            .Skip(Math.Max(1, keep))
            .ToListAsync();
        if (stale.Count == 0) return;
        _context.GeneratedMixes.RemoveRange(stale);
        await _context.SaveChangesAsync();
    }
}
