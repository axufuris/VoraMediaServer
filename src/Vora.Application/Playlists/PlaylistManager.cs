using Vora.Application.Media.SmartPlaylists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists;

public interface IPlaylistManager
{
    Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId, PlaylistAccessFilter access);
    Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid viewerProfileId, PlaylistAccessFilter access);
    Task<List<PlaylistSummaryVM>> GetSharedByOthersAsync(Guid viewerProfileId, PlaylistAccessFilter access);
    Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared);
    Task<Guid?> CopyPlaylistAsync(Guid sourceId, Guid viewerProfileId, PlaylistAccessFilter access);
    Task<Guid> CreatePlaylistAsync(Guid profileId, string name, string? description, PlaylistMediaType mediaType);
    Task AddToPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);
    Task RemoveFromPlaylistAsync(Guid playlistId, Guid profileId, Guid playlistItemId);
    Task ReorderPlaylistAsync(Guid playlistId, Guid profileId, List<Guid> itemIds);
    Task<bool> MarkAllUnplayedAsync(Guid playlistId, Guid profileId, PlaylistAccessFilter access);
    Task DeletePlaylistAsync(Guid playlistId, Guid profileId);
    Task<string?> SetImageAsync(Guid playlistId, Guid ownerProfileId, byte[] bytes);
    Task<bool> RemoveImageAsync(Guid playlistId, Guid ownerProfileId);
    Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId);
    Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);
    Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description);
}

public class PlaylistManager : IPlaylistManager
{
    private readonly IPlaylistRepository _repository;
    private readonly IPlaylistImageStore _images;

    public PlaylistManager(IPlaylistRepository repository, IPlaylistImageStore images)
    {
        _repository = repository;
        _images = images;
    }

    public Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId, PlaylistAccessFilter access) =>
        _repository.GetPlaylistsAsync(profileId, access);

    public Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid viewerProfileId, PlaylistAccessFilter access) =>
        _repository.GetPlaylistDetailsAsync(id, viewerProfileId, access);

    public Task<List<PlaylistSummaryVM>> GetSharedByOthersAsync(Guid viewerProfileId, PlaylistAccessFilter access) =>
        _repository.GetSharedByOthersAsync(viewerProfileId, access);

    public Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared) =>
        _repository.SetSharedAsync(id, ownerProfileId, isShared);

    public Task<Guid?> CopyPlaylistAsync(Guid sourceId, Guid viewerProfileId, PlaylistAccessFilter access) =>
        _repository.CopyPlaylistAsync(sourceId, viewerProfileId, access);

    public async Task<Guid> CreatePlaylistAsync(Guid profileId, string name, string? description, PlaylistMediaType mediaType)
    {
        var playlist = new Playlist { ProfileId = profileId, Name = name, Description = description, MediaType = mediaType };
        return await _repository.CreatePlaylistAsync(playlist);
    }

    public async Task AddToPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId)
    {
        if (!await _repository.IsPlaylistOwnerAsync(playlistId, profileId)) return;

        int nextOrder = await _repository.GetMaxItemOrderAsync(playlistId) + 1;

        await _repository.AddPlaylistItemAsync(new PlaylistItem { PlaylistId = playlistId, MediaItemId = mediaItemId, Order = nextOrder });
        await _repository.TouchPlaylistAsync(playlistId);
    }

    public async Task RemoveFromPlaylistAsync(Guid playlistId, Guid profileId, Guid playlistItemId)
    {
        await _repository.RemovePlaylistItemAsync(playlistId, profileId, playlistItemId);
    }

    public async Task ReorderPlaylistAsync(Guid playlistId, Guid profileId, List<Guid> itemIds)
    {
        var playlist = await _repository.GetPlaylistWithItemsAsync(playlistId, profileId);
        if (playlist == null) return;

        var items = playlist.Items.ToDictionary(i => i.Id);
        for (int i = 0; i < itemIds.Count; i++)
        {
            if (items.TryGetValue(itemIds[i], out var item))
            {
                item.Order = i;
            }
        }

        playlist.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdatePlaylistAsync(playlist);
    }

    // A read of the playlist and a write to the caller's own watch state, so a
    // viewer of a shared playlist may do it too. False when the playlist is
    // neither theirs nor shared.
    public async Task<bool> MarkAllUnplayedAsync(Guid playlistId, Guid profileId, PlaylistAccessFilter access)
    {
        var mediaIds = await _repository.GetVisiblePlaylistMediaIdsAsync(playlistId, profileId, access);
        if (mediaIds == null) return false;
        if (mediaIds.Count > 0) await _repository.MarkItemsUnplayedAsync(profileId, mediaIds);
        return true;
    }

    public async Task DeletePlaylistAsync(Guid playlistId, Guid profileId)
    {
        var deletion = await _repository.DeletePlaylistAsync(playlistId, profileId);
        if (deletion.Found) await ReleaseImageAsync(deletion.ImageUrl);
    }

    // Null when the bytes aren't an image the artwork route can serve, or the
    // playlist isn't the caller's; the endpoint has already told the two apart.
    public async Task<string?> SetImageAsync(Guid playlistId, Guid ownerProfileId, byte[] bytes)
    {
        if (!await _repository.IsPlaylistOwnerAsync(playlistId, ownerProfileId)) return null;

        var url = await _images.SaveAsync(playlistId, bytes);
        if (url == null) return null;

        var change = await _repository.SetImageAsync(playlistId, ownerProfileId, url);
        if (!change.Found)
        {
            _images.Delete(url);
            return null;
        }

        await ReleaseImageAsync(change.PreviousImageUrl);
        return url;
    }

    // Back to the mosaic built from the playlist's items.
    public async Task<bool> RemoveImageAsync(Guid playlistId, Guid ownerProfileId)
    {
        var change = await _repository.SetImageAsync(playlistId, ownerProfileId, null);
        if (!change.Found) return false;

        await ReleaseImageAsync(change.PreviousImageUrl);
        return true;
    }

    private async Task ReleaseImageAsync(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return;
        if (await _repository.IsImageInUseAsync(imageUrl)) return;
        _images.Delete(imageUrl);
    }

    public async Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId)
    {
        return await _repository.GetPlaylistsContainingItemAsync(profileId, mediaItemId);
    }

    public async Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId)
    {
        await _repository.RemoveMediaFromPlaylistAsync(playlistId, profileId, mediaItemId);
    }

    public async Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description)
    {
        await _repository.UpdatePlaylistDetailsAsync(id, profileId, name, description);
    }
}