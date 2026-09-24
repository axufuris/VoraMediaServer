using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public interface ISmartPlaylistRepository
{
    Task<List<SmartPlaylist>> GetForProfileAsync(Guid profileId);
    Task<SmartPlaylist?> GetByIdAsync(Guid id, Guid profileId);
    Task<SmartPlaylist?> GetVisibleAsync(Guid id, Guid viewerProfileId);
    Task<List<SmartPlaylist>> GetSharedByOthersAsync(Guid viewerProfileId);
    Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared);
    Task AddAsync(SmartPlaylist playlist);
    Task UpdateAsync(SmartPlaylist playlist);
    Task DeleteAsync(Guid id, Guid profileId);
}
