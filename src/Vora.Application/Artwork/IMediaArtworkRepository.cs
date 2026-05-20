using Vora.Domain.Entities.Media;

namespace Vora.Application.Artwork;

public interface IMediaArtworkRepository
{
    Task<MediaArtwork?> GetArtworkByIdAsync(Guid id);
    Task<IEnumerable<MediaArtwork>> GetMediaArtworkAsync(Guid mediaItemId);
    Task ReplaceMediaArtworkAsync(Guid mediaItemId, IEnumerable<MediaArtwork> artwork);
    Task ClearArtworkForLibraryAsync(Guid libraryId);
    Task AddMediaArtworkAsync(MediaArtwork artwork);
    Task DeleteMediaArtworkAsync(Guid id);
}
