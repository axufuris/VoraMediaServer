using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Vora.Application.Libraries;
using Vora.Application.Posters;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class LibraryRepository : ILibraryRepository
{
    private readonly VoraDbContext _context;
    private readonly IOverlaySweepService _overlaySweep;

    public LibraryRepository(VoraDbContext context, IOverlaySweepService overlaySweep)
    {
        _context = context;
        _overlaySweep = overlaySweep;
    }

    public async Task<T?> GetProjectedByIdAsync<T>(Guid id, Expression<Func<MediaLibrary, T>> projection)
    {
        return await _context.MediaLibraries
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(projection)
            .FirstOrDefaultAsync();
    }

    public async Task<MediaLibrary?> GetForUpdateAsync(Guid id)
    {
        return await _context.MediaLibraries.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<IEnumerable<T>> GetAllProjectedAsync<T>(Expression<Func<MediaLibrary, T>> projection, bool hasAllAccess = true, List<Guid>? allowedLibs = null)
    {
        var query = _context.MediaLibraries.AsNoTracking();

        if (!hasAllAccess && allowedLibs != null)
        {
            query = query.Where(l => allowedLibs.Contains(l.Id));
        }

        return await query.Select(projection).ToListAsync();
    }

    public async Task<IEnumerable<MediaLibrary>> GetAllLibrariesAsync()
    {
        return await _context.MediaLibraries.AsNoTracking().ToListAsync();
    }

    public async Task<Guid> CreateLibraryAsync(MediaLibrary library)
    {
        await _context.MediaLibraries.AddAsync(library);
        await _context.SaveChangesAsync();
        return library.Id;
    }

    public async Task UpdateLibraryAsync(MediaLibrary library)
    {
        _context.MediaLibraries.Update(library);
        await _context.SaveChangesAsync();
    }

    public async Task CleanUpOrphanedMediaAsync(Guid libraryId)
    {
        var library = await _context.MediaLibraries.FindAsync(libraryId);
        if (library == null) return;

        var validFolders = library.FolderPaths ?? new List<string>();

        await CleanUpOrphanedExtrasAsync(libraryId, validFolders);

        var allParts = await _context.MediaParts
            .Where(p => p.MediaItem != null && p.MediaItem.LibraryId == libraryId)
            .Select(p => new { p.Id, p.FilePath, p.MediaItemId })
            .ToListAsync();

        var orphanedPartIds = allParts
            .Where(p => !validFolders.Any(f => p.FilePath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id)
            .ToList();

        if (!orphanedPartIds.Any()) return;

        var orphanedItemIds = allParts
            .Where(p => orphanedPartIds.Contains(p.Id))
            .Select(p => p.MediaItemId)
            .Distinct()
            .ToList();

        var directItemsToDelete = await _context.MediaItems
            .AsNoTracking()
            .Where(m => orphanedItemIds.Contains(m.Id) && m.MediaParts.Count(p => !orphanedPartIds.Contains(p.Id)) == 0)
            .Select(m => new { m.Id, m.PosterUrl, m.BackgroundUrl, Type = m.GetType().Name })
            .ToListAsync();

        var episodesToDelete = directItemsToDelete.Where(i => i.Type == "Episode").Select(i => i.Id).ToList();
        var moviesToDelete = directItemsToDelete.Where(i => i.Type == "Movie").Select(i => i.Id).ToList();
        var tracksToDelete = directItemsToDelete.Where(i => i.Type == "Track").Select(i => i.Id).ToList();

        var urlsToSweep = directItemsToDelete.Select(i => i.PosterUrl).Concat(directItemsToDelete.Select(i => i.BackgroundUrl)).ToList();

        var seasonsToDelete = new List<Guid>();
        var showsToDelete = new List<Guid>();
        var albumsToDelete = new List<Guid>();
        var artistsToDelete = new List<Guid>();

        if (episodesToDelete.Any())
        {
            var allSeasons = await _context.Set<Season>()
                .AsNoTracking()
                .Where(s => s.LibraryId == libraryId)
                .Select(s => new { s.Id, s.TvShowId, s.PosterUrl, s.BackgroundUrl, Episodes = _context.Set<Episode>().Where(e => e.SeasonId == s.Id).Select(e => e.Id).ToList() })
                .ToListAsync();

            var emptySeasons = allSeasons.Where(s => s.Episodes.All(e => episodesToDelete.Contains(e))).ToList();
            seasonsToDelete = emptySeasons.Select(s => s.Id).ToList();

            urlsToSweep.AddRange(emptySeasons.Select(s => s.PosterUrl));
            urlsToSweep.AddRange(emptySeasons.Select(s => s.BackgroundUrl));

            if (seasonsToDelete.Any())
            {
                var allShows = await _context.Set<TvShow>()
                    .AsNoTracking()
                    .Where(t => t.LibraryId == libraryId)
                    .Select(t => new { t.Id, t.PosterUrl, t.BackgroundUrl, Seasons = _context.Set<Season>().Where(s => s.TvShowId == t.Id).Select(s => s.Id).ToList() })
                    .ToListAsync();

                var emptyShows = allShows.Where(t => t.Seasons.All(s => seasonsToDelete.Contains(s))).ToList();
                showsToDelete = emptyShows.Select(t => t.Id).ToList();

                urlsToSweep.AddRange(emptyShows.Select(t => t.PosterUrl));
                urlsToSweep.AddRange(emptyShows.Select(t => t.BackgroundUrl));
            }
        }

        {
            var orphanedAlbums = await _context.Set<Album>()
                .AsNoTracking()
                .Where(a => a.LibraryId == libraryId
                    && !_context.Set<Track>().Any(t => t.AlbumId == a.Id && !tracksToDelete.Contains(t.Id)))
                .Select(a => new { a.Id, a.ArtworkUrl, a.BackgroundUrl })
                .ToListAsync();

            albumsToDelete = orphanedAlbums.Select(a => a.Id).ToList();
            urlsToSweep.AddRange(orphanedAlbums.Select(a => a.ArtworkUrl));
            urlsToSweep.AddRange(orphanedAlbums.Select(a => a.BackgroundUrl));

            var orphanedArtists = await _context.Set<Artist>()
                .AsNoTracking()
                .Where(a => a.LibraryId == libraryId
                    && !_context.Set<Album>().Any(al => al.ArtistId == a.Id && !albumsToDelete.Contains(al.Id)))
                .Select(a => new { a.Id, a.ArtworkUrl, a.BackgroundUrl })
                .ToListAsync();

            artistsToDelete = orphanedArtists.Select(a => a.Id).ToList();
            urlsToSweep.AddRange(orphanedArtists.Select(a => a.ArtworkUrl));
            urlsToSweep.AddRange(orphanedArtists.Select(a => a.BackgroundUrl));
        }

        _overlaySweep.SweepPhysicalOverlays(urlsToSweep);

        await _context.MediaParts.Where(p => orphanedPartIds.Contains(p.Id)).ExecuteDeleteAsync();

        if (episodesToDelete.Any() || moviesToDelete.Any() || tracksToDelete.Any())
        {
            var directIds = episodesToDelete.Concat(moviesToDelete).Concat(tracksToDelete).ToList();
            await _context.MediaItems.Where(m => directIds.Contains(m.Id)).ExecuteDeleteAsync();
        }

        if (seasonsToDelete.Any())
        {
            await _context.Set<Season>().Where(s => seasonsToDelete.Contains(s.Id)).ExecuteDeleteAsync();
        }

        if (showsToDelete.Any())
        {
            await _context.Set<TvShow>().Where(t => showsToDelete.Contains(t.Id)).ExecuteDeleteAsync();
        }

        if (albumsToDelete.Any())
        {
            await _context.Set<Album>().Where(a => albumsToDelete.Contains(a.Id)).ExecuteDeleteAsync();
        }

        if (artistsToDelete.Any())
        {
            await _context.Set<Artist>().Where(a => artistsToDelete.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    private async Task CleanUpOrphanedExtrasAsync(Guid libraryId, List<string> validFolders)
    {
        var extraParts = await _context.MediaParts
            .Where(p => p.MediaExtra != null && p.MediaExtra.MediaItem.LibraryId == libraryId)
            .Select(p => new { p.Id, p.FilePath, p.MediaExtraId })
            .ToListAsync();

        var orphanedPartIds = extraParts
            .Where(p => !validFolders.Any(f => p.FilePath.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id)
            .ToList();

        if (!orphanedPartIds.Any()) return;

        var affectedExtraIds = extraParts
            .Where(p => orphanedPartIds.Contains(p.Id))
            .Select(p => p.MediaExtraId)
            .Distinct()
            .ToList();

        await _context.MediaParts.Where(p => orphanedPartIds.Contains(p.Id)).ExecuteDeleteAsync();

        await _context.MediaExtras
            .Where(e => affectedExtraIds.Contains(e.Id) && !_context.MediaParts.Any(p => p.MediaExtraId == e.Id))
            .ExecuteDeleteAsync();
    }

    public async Task DeleteLibraryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var library = await _context.MediaLibraries.FindAsync(new object?[] { id }, cancellationToken);
        if (library == null) return;

        var itemsToDelete = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.LibraryId == id)
            .Select(m => new { m.PosterUrl, m.BackgroundUrl })
            .ToListAsync(cancellationToken);

        _overlaySweep.SweepPhysicalOverlays(itemsToDelete.Select(i => i.PosterUrl).Concat(itemsToDelete.Select(i => i.BackgroundUrl)));

        var showIds = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.LibraryId == id && m is TvShow)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (showIds.Any())
        {
            await _context.Set<Season>()
                .Where(s => showIds.Contains(s.TvShowId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await _context.MediaItems
            .Where(m => m.LibraryId == id)
            .ExecuteDeleteAsync(cancellationToken);

        _context.MediaLibraries.Remove(library);
        await _context.SaveChangesAsync(cancellationToken);
    }

}