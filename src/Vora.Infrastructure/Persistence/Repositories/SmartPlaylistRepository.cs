using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.SmartPlaylists;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Persistence.Repositories;

public sealed class SmartPlaylistRepository : ISmartPlaylistRepository
{
    private readonly VoraDbContext _context;

    public SmartPlaylistRepository(VoraDbContext context)
    {
        _context = context;
    }

    public Task<List<SmartPlaylist>> GetForProfileAsync(Guid profileId) =>
        _context.SmartPlaylists
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .OrderBy(p => p.Name)
            .ToListAsync();

    // Owner only, and deliberately left that way: the update path loads through
    // this before saving, so widening it to shared playlists would let anyone
    // rewrite someone else's rules. Reads that should see shared playlists go
    // through GetVisibleAsync instead.
    public Task<SmartPlaylist?> GetByIdAsync(Guid id, Guid profileId) =>
        _context.SmartPlaylists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.ProfileId == profileId);

    public Task<SmartPlaylist?> GetVisibleAsync(Guid id, Guid viewerProfileId) =>
        _context.SmartPlaylists
            .AsNoTracking()
            .Include(p => p.Profile)
            .FirstOrDefaultAsync(p => p.Id == id && (p.ProfileId == viewerProfileId || p.IsShared));

    public Task<List<SmartPlaylist>> GetSharedByOthersAsync(Guid viewerProfileId) =>
        _context.SmartPlaylists
            .AsNoTracking()
            .Include(p => p.Profile)
            .Where(p => p.IsShared && p.ProfileId != viewerProfileId)
            .OrderByDescending(p => p.SharedAt)
            .ToListAsync();

    public async Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared)
    {
        var entity = await _context.SmartPlaylists.FirstOrDefaultAsync(p => p.Id == id && p.ProfileId == ownerProfileId);
        if (entity == null) return false;

        if (entity.IsShared != isShared)
        {
            entity.IsShared = isShared;
            entity.SharedAt = isShared ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync();
        }

        return true;
    }

    public async Task AddAsync(SmartPlaylist playlist)
    {
        playlist.CreatedAt = DateTime.UtcNow;
        playlist.UpdatedAt = playlist.CreatedAt;
        await _context.SmartPlaylists.AddAsync(playlist);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(SmartPlaylist playlist)
    {
        var tracked = await _context.SmartPlaylists.FirstOrDefaultAsync(p => p.Id == playlist.Id && p.ProfileId == playlist.ProfileId);
        if (tracked == null) return;
        tracked.Name = playlist.Name;
        tracked.Description = playlist.Description;
        tracked.ArtworkUrl = playlist.ArtworkUrl;
        tracked.RulesJson = playlist.RulesJson;
        tracked.Limit = playlist.Limit;
        tracked.SortBy = playlist.SortBy;
        tracked.SortDirection = playlist.SortDirection;
        tracked.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id, Guid profileId)
    {
        var entity = await _context.SmartPlaylists.FirstOrDefaultAsync(p => p.Id == id && p.ProfileId == profileId);
        if (entity == null) return;
        _context.SmartPlaylists.Remove(entity);
        await _context.SaveChangesAsync();
    }
}
