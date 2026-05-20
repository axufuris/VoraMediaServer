using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Media.SmartPlaylists;

public interface ISmartPlaylistRepository
{
    Task<List<SmartPlaylist>> GetForProfileAsync(Guid profileId);
    Task<SmartPlaylist?> GetByIdAsync(Guid id, Guid profileId);
    Task AddAsync(SmartPlaylist playlist);
    Task UpdateAsync(SmartPlaylist playlist);
    Task DeleteAsync(Guid id, Guid profileId);
}
