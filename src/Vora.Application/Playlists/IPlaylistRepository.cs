using System;
using System.Collections.Generic;
using System.Text;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists;

public interface IPlaylistRepository
{
    Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId);
    Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid profileId);
    Task<Guid> CreatePlaylistAsync(Playlist playlist);

    Task<bool> IsPlaylistOwnerAsync(Guid playlistId, Guid profileId);
    Task<int> GetMaxItemOrderAsync(Guid playlistId);
    Task TouchPlaylistAsync(Guid playlistId);

    Task<Playlist?> GetPlaylistWithItemsAsync(Guid playlistId, Guid profileId);
    Task AddPlaylistItemAsync(PlaylistItem item);
    Task UpdatePlaylistAsync(Playlist playlist);
    Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description);

    Task RemovePlaylistItemAsync(Guid playlistId, Guid profileId, Guid playlistItemId);
    Task DeletePlaylistAsync(Guid playlistId, Guid profileId);
    Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);

    Task<List<Guid>> GetPlaylistMediaIdsAsync(Guid playlistId, Guid profileId);
    Task MarkItemsUnplayedAsync(Guid profileId, List<Guid> mediaIds);
    Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId);
}