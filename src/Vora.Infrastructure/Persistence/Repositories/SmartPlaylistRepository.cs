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

    public Task<SmartPlaylist?> GetByIdAsync(Guid id, Guid profileId) =>
        _context.SmartPlaylists
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.ProfileId == profileId);

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
