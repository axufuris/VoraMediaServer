using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;

namespace Vora.Application.Playlists;

public interface IPlaylistManager
{
    Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId);
    Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid profileId);
    Task<Guid> CreatePlaylistAsync(Guid profileId, string name, string? description, PlaylistMediaType mediaType);
    Task AddToPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);
    Task RemoveFromPlaylistAsync(Guid playlistId, Guid profileId, Guid playlistItemId);
    Task ReorderPlaylistAsync(Guid playlistId, Guid profileId, List<Guid> itemIds);
    Task MarkAllUnplayedAsync(Guid playlistId, Guid profileId);
    Task DeletePlaylistAsync(Guid playlistId, Guid profileId);
    Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId);
    Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId);
    Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description);
}

public class PlaylistManager : IPlaylistManager
{
    private readonly IPlaylistRepository _repository;

    public PlaylistManager(IPlaylistRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId)
    {
        return await _repository.GetPlaylistsAsync(profileId);
    }

    public async Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid profileId)
    {
        return await _repository.GetPlaylistDetailsAsync(id, profileId);
    }

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

    public async Task MarkAllUnplayedAsync(Guid playlistId, Guid profileId)
    {
        var mediaIds = await _repository.GetPlaylistMediaIdsAsync(playlistId, profileId);
        if (!mediaIds.Any()) return;
        await _repository.MarkItemsUnplayedAsync(profileId, mediaIds);
    }

    public async Task DeletePlaylistAsync(Guid playlistId, Guid profileId)
    {
        await _repository.DeletePlaylistAsync(playlistId, profileId);
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