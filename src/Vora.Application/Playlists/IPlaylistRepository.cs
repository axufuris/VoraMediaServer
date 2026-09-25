using System;
using System.Collections.Generic;
using System.Text;
using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists;

public interface IPlaylistRepository
{
    Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId, PlaylistAccessFilter access);
    Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid viewerProfileId, PlaylistAccessFilter access);
    Task<List<PlaylistSummaryVM>> GetSharedByOthersAsync(Guid viewerProfileId, PlaylistAccessFilter access);
    Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared);
    Task<Guid?> CopyPlaylistAsync(Guid sourceId, Guid viewerProfileId, PlaylistAccessFilter access);
    Task<Guid> CreatePlaylistAsync(Playlist playlist);

    Task<bool> IsPlaylistOwnerAsync(Guid playlistId, Guid profileId);
    Task<int> GetMaxItemOrderAsync(Guid playlistId);
    Task TouchPlaylistAsync(Guid playlistId);

    Task<Playlist?> GetPlaylistWithItemsAsync(Guid playlistId, Guid profileId);
    Task AddPlaylistItemAsync(PlaylistItem item);
    Task UpdatePlaylistAsync(Playlist playlist);
    Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description);

    Task RemovePlaylistItemAsync(Guid playlistId, Guid profileId, Guid playlistItemId);
    Task<PlaylistDeletion> DeletePlaylistAsync(Guid playlistId, Guid profileId);
    Task<PlaylistImageChange> SetImageAsync(Guid playlistId, Guid ownerProfileId, string? imageUrl);
    Task<bool> IsImageInUseAsync(string imageUrl);
    Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);

    Task<List<Guid>> GetPlaylistMediaIdsAsync(Guid playlistId, Guid profileId);
    Task<List<Guid>?> GetVisiblePlaylistMediaIdsAsync(Guid playlistId, Guid viewerProfileId, PlaylistAccessFilter access);
    Task MarkItemsUnplayedAsync(Guid profileId, List<Guid> mediaIds);
    Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId);
}

public sealed record PlaylistDeletion(bool Found, string? ImageUrl)
{
    public static PlaylistDeletion NotFound { get; } = new(false, null);
}

public sealed record PlaylistImageChange(bool Found, string? PreviousImageUrl)
{
    public static PlaylistImageChange NotFound { get; } = new(false, null);
}
