using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MediaDedupeRepository : IMediaDedupeRepository
{
    private readonly VoraDbContext _context;

    public MediaDedupeRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<MediaItem>> GetMediaItemsWithMultiplePartsAsync()
    {
        var movies = await _context.MediaItems.OfType<Movie>()
            .Include(m => m.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(m => m.MediaParts).ThenInclude(p => p.AudioTracks)
            .Where(m => m.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        var episodes = await _context.MediaItems.OfType<Episode>()
            .Include(e => e.Season).ThenInclude(s => s.TvShow)
            .Include(e => e.MediaParts).ThenInclude(p => p.VideoTracks)
            .Include(e => e.MediaParts).ThenInclude(p => p.AudioTracks)
            .Where(e => e.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        var tracks = await _context.MediaItems.OfType<Track>()
            .Include(t => t.Album).ThenInclude(a => a!.Artist)
            .Include(t => t.MediaParts)
            .Where(t => t.MediaParts.Count > 1)
            .AsNoTracking().ToListAsync();

        return movies.Cast<MediaItem>()
            .Concat(episodes)
            .Concat(tracks)
            .ToList();
    }

    public async Task<MediaPart?> GetMediaPartByIdAsync(Guid partId)
    {
        return await _context.MediaParts.FirstOrDefaultAsync(p => p.Id == partId);
    }

    public async Task DeleteMediaPartAsync(MediaPart part)
    {
        _context.MediaParts.Remove(part);
        await _context.SaveChangesAsync();
    }

    public async Task<MediaDedupeSettings?> GetGlobalSettingsAsync()
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryId == null);
    }

    public async Task<MediaDedupeSettings?> GetLibraryOverrideAsync(Guid libraryId)
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.LibraryId == libraryId);
    }

    public async Task<List<MediaDedupeSettings>> GetAllLibraryOverridesAsync()
    {
        return await _context.MediaDedupeSettings
            .AsNoTracking()
            .Where(s => s.LibraryId != null)
            .ToListAsync();
    }

    public async Task<MediaDedupeSettings> UpsertSettingsAsync(MediaDedupeSettings settings)
    {
        var existing = await _context.MediaDedupeSettings
            .FirstOrDefaultAsync(s => s.LibraryId == settings.LibraryId);

        settings.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            _context.MediaDedupeSettings.Add(settings);
        }
        else
        {
            settings.Id = existing.Id;
            _context.Entry(existing).CurrentValues.SetValues(settings);
        }

        await _context.SaveChangesAsync();
        return settings;
    }

    public async Task DeleteLibraryOverrideAsync(Guid libraryId)
    {
        var existing = await _context.MediaDedupeSettings
            .FirstOrDefaultAsync(s => s.LibraryId == libraryId);
        if (existing == null) return;

        _context.MediaDedupeSettings.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<List<MediaDedupeIgnoredGroup>> GetIgnoredGroupsAsync()
    {
        var groups = await _context.MediaDedupeIgnoredGroups
            .Include(g => g.MediaItem)
            .AsNoTracking()
            .OrderByDescending(g => g.IgnoredAt)
            .ToListAsync();

        var episodeIds = groups
            .Where(g => g.MediaItem is Episode)
            .Select(g => g.MediaItemId)
            .ToList();

        if (episodeIds.Count > 0)
        {
            var episodes = await _context.MediaItems.OfType<Episode>()
                .Include(e => e.Season).ThenInclude(s => s.TvShow)
                .AsNoTracking()
                .Where(e => episodeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            foreach (var group in groups)
            {
                if (group.MediaItem is Episode && episodes.TryGetValue(group.MediaItemId, out var hydrated))
                {
                    group.MediaItem = hydrated;
                }
            }
        }

        var trackIds = groups
            .Where(g => g.MediaItem is Track)
            .Select(g => g.MediaItemId)
            .ToList();

        if (trackIds.Count > 0)
        {
            var tracks = await _context.MediaItems.OfType<Track>()
                .Include(t => t.Album).ThenInclude(a => a!.Artist)
                .AsNoTracking()
                .Where(t => trackIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id);

            foreach (var group in groups)
            {
                if (group.MediaItem is Track && tracks.TryGetValue(group.MediaItemId, out var hydrated))
                {
                    group.MediaItem = hydrated;
                }
            }
        }

        return groups;
    }

    public async Task<MediaDedupeIgnoredGroup?> GetIgnoredGroupAsync(Guid mediaItemId, string resolution)
    {
        return await _context.MediaDedupeIgnoredGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.MediaItemId == mediaItemId && g.Resolution == resolution);
    }

    public async Task AddIgnoredGroupAsync(MediaDedupeIgnoredGroup group)
    {
        _context.MediaDedupeIgnoredGroups.Add(group);
        await _context.SaveChangesAsync();
    }

    public async Task RemoveIgnoredGroupAsync(Guid ignoredGroupId)
    {
        var existing = await _context.MediaDedupeIgnoredGroups
            .FirstOrDefaultAsync(g => g.Id == ignoredGroupId);
        if (existing == null) return;

        _context.MediaDedupeIgnoredGroups.Remove(existing);
        await _context.SaveChangesAsync();
    }
}
