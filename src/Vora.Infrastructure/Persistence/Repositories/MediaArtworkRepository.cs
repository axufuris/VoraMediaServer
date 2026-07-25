using Microsoft.EntityFrameworkCore;
using Vora.Application.Artwork;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MediaArtworkRepository(VoraDbContext context) : IMediaArtworkRepository
{
    public Task<MediaArtwork?> GetArtworkByIdAsync(Guid id) =>
        context.Set<MediaArtwork>().FindAsync(id).AsTask();

    public async Task<IEnumerable<MediaArtwork>> GetMediaArtworkAsync(Guid mediaItemId) =>
        await context.Set<MediaArtwork>()
            .AsNoTracking()
            .Where(a => a.MediaItemId == mediaItemId)
            .ToListAsync();

    public async Task ReplaceMediaArtworkAsync(Guid mediaItemId, IEnumerable<MediaArtwork> artwork)
    {
        var existing = await context.Set<MediaArtwork>().Where(a => a.MediaItemId == mediaItemId).ToListAsync();
        context.Set<MediaArtwork>().RemoveRange(existing);
        await context.Set<MediaArtwork>().AddRangeAsync(artwork);
        await context.SaveChangesAsync();
    }

    // Replaces only provider-sourced rows, preserving the user's manual uploads.
    public async Task ReplaceProviderMediaArtworkAsync(Guid mediaItemId, IEnumerable<MediaArtwork> artwork)
    {
        var existing = await context.Set<MediaArtwork>()
            .Where(a => a.MediaItemId == mediaItemId && !a.IsUserUploaded)
            .ToListAsync();
        context.Set<MediaArtwork>().RemoveRange(existing);
        await context.Set<MediaArtwork>().AddRangeAsync(artwork);
        await context.SaveChangesAsync();
    }

    public Task ClearArtworkForLibraryAsync(Guid libraryId) =>
        context.Set<MediaArtwork>()
            .Where(a => a.MediaItem.LibraryId == libraryId && !a.IsUserUploaded)
            .ExecuteDeleteAsync();

    public async Task AddMediaArtworkAsync(MediaArtwork artwork)
    {
        await context.Set<MediaArtwork>().AddAsync(artwork);
        await context.SaveChangesAsync();
    }

    public async Task DeleteMediaArtworkAsync(Guid id)
    {
        var artwork = await context.Set<MediaArtwork>().FindAsync(id);
        if (artwork == null)
        {
            return;
        }

        context.Set<MediaArtwork>().Remove(artwork);
        await context.SaveChangesAsync();
    }
}
