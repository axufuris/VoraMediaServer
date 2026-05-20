using Microsoft.EntityFrameworkCore;
using Vora.Application.Playlists;
using Vora.Application.Playlists.ViewModels;
using Vora.Domain.Entities.Playlists;

namespace Vora.Infrastructure.Persistence.Repositories;

public class PlaylistRepository : IPlaylistRepository
{
    private readonly VoraDbContext _context;

    public PlaylistRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId)
    {
        return await _context.Playlists
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .OrderBy(p => p.Name)
            .Select(p => new PlaylistSummaryVM
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                MediaType = p.MediaType,
                ItemCount = p.Items.Count,
                PosterUrls = p.Items.OrderBy(i => i.Order)
                                    .Select(i => i.MediaItem.PosterUrl
                                        ?? (i.MediaItem is Vora.Domain.Entities.Media.Track
                                            ? ((Vora.Domain.Entities.Media.Track)i.MediaItem).Album!.ArtworkUrl
                                            : null))
                                    .Where(u => u != null)
                                    .Select(u => u!)
                                    .Take(4)
                                    .ToList()
            })
            .ToListAsync();
    }

    public async Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid profileId)
    {
        var playlist = await _context.Playlists
            .AsNoTracking()
            .Where(p => p.Id == id && p.ProfileId == profileId)
            .Select(p => new PlaylistDetailsVM
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                ItemCount = p.Items.Count,
                PosterUrls = p.Items.OrderBy(i => i.Order)
                                    .Select(i => i.MediaItem.PosterUrl
                                        ?? (i.MediaItem is Vora.Domain.Entities.Media.Track
                                            ? ((Vora.Domain.Entities.Media.Track)i.MediaItem).Album!.ArtworkUrl
                                            : null))
                                    .Where(u => u != null)
                                    .Select(u => u!)
                                    .Take(4)
                                    .ToList()
            })
            .FirstOrDefaultAsync();

        if (playlist == null) return null;

        var rawItems = await _context.PlaylistItems
            .AsNoTracking()
            .Include(i => i.MediaItem)
                .ThenInclude(m => m.Analysis)
            .Where(i => i.PlaylistId == id)
            .OrderBy(i => i.Order)
            .ToListAsync();

        var episodeIds = rawItems.Where(i => i.MediaItem is Vora.Domain.Entities.Media.Episode).Select(i => i.MediaItem.Id).ToList();
        var seasonIds = rawItems.Where(i => i.MediaItem is Vora.Domain.Entities.Media.Season).Select(i => i.MediaItem.Id).ToList();
        var trackIds = rawItems.Where(i => i.MediaItem is Vora.Domain.Entities.Media.Track).Select(i => i.MediaItem.Id).ToList();

        var tvMetadata = new Dictionary<Guid, (string ShowTitle, int? SeasonNum, int? EpNum, string ContentRating)>();
        var trackMetadata = new Dictionary<Guid, (string? ArtistName, string? AlbumTitle, Guid? AlbumId, string? AlbumArtworkUrl, int TrackNumber, int? DurationSeconds)>();

        if (episodeIds.Any())
        {
            var eps = await _context.Set<Vora.Domain.Entities.Media.Episode>()
                .Include(e => e.Season).ThenInclude(s => s.TvShow)
                .Where(e => episodeIds.Contains(e.Id))
                .ToListAsync();

            foreach (var e in eps)
                tvMetadata[e.Id] = (e.Season.TvShow.Title, e.Season.SeasonNumber, e.EpisodeNumber, e.Season.TvShow.ContentRating ?? "");
        }

        if (seasonIds.Any())
        {
            var seasons = await _context.Set<Vora.Domain.Entities.Media.Season>()
                .Include(s => s.TvShow)
                .Where(s => seasonIds.Contains(s.Id))
                .ToListAsync();

            foreach (var s in seasons)
                tvMetadata[s.Id] = (s.TvShow.Title, s.SeasonNumber, null, s.TvShow.ContentRating ?? "");
        }

        if (trackIds.Any())
        {
            var trackMeta = await _context.Set<Vora.Domain.Entities.Media.Track>()
                .AsNoTracking()
                .Include(t => t.Album)
                    .ThenInclude(a => a!.Artist)
                .Where(t => trackIds.Contains(t.Id))
                .ToListAsync();

            foreach (var t in trackMeta)
            {
                trackMetadata[t.Id] = (
                    t.Album?.Artist?.Name,
                    t.Album?.Title,
                    t.AlbumId,
                    t.Album?.ArtworkUrl,
                    t.TrackNumber,
                    t.DurationSeconds
                );
            }
        }

        var mediaIds = rawItems.Select(i => i.MediaItemId).Distinct().ToList();
        var userStates = await _context.UserMediaStates
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId && mediaIds.Contains(s.MediaItemId))
            .ToDictionaryAsync(s => s.MediaItemId);

        playlist.Items = rawItems.Select(i =>
        {
            var hasTvMeta = tvMetadata.TryGetValue(i.MediaItemId, out var meta);
            var hasTrackMeta = trackMetadata.TryGetValue(i.MediaItemId, out var trackInfo);
            var state = userStates.TryGetValue(i.MediaItemId, out var s) ? s : null;

            return new PlaylistItemVM
            {
                Id = i.Id,
                MediaItemId = i.MediaItemId,
                Order = i.Order,
                Title = i.MediaItem.Title,
                TvShowTitle = hasTvMeta ? meta.ShowTitle : null,
                SeasonNumber = hasTvMeta ? meta.SeasonNum : null,
                EpisodeNumber = hasTvMeta ? meta.EpNum : null,
                ReleaseYear = i.MediaItem.ReleaseDate?.Year,
                Type = i.MediaItem is Vora.Domain.Entities.Media.Movie ? "Movie" :
                       i.MediaItem is Vora.Domain.Entities.Media.Episode ? "Episode" :
                       i.MediaItem is Vora.Domain.Entities.Media.Season ? "Season" :
                       i.MediaItem is Vora.Domain.Entities.Media.TvShow ? "TvShow" :
                       i.MediaItem is Vora.Domain.Entities.Media.Track ? "Track" : "Unknown",
                ContentRating = hasTvMeta && !string.IsNullOrEmpty(meta.ContentRating) ? meta.ContentRating : i.MediaItem.ContentRating,
                PosterUrl = i.MediaItem.PosterUrl,
                BackgroundUrl = i.MediaItem.BackgroundUrl,
                DurationMinutes = i.MediaItem.Analysis?.Duration.HasValue == true ? (int)i.MediaItem.Analysis.Duration.Value.TotalMinutes : (int?)null,
                IsPlayed = state?.IsPlayed ?? false,
                ResumePositionSeconds = state?.ResumePositionSeconds ?? 0,
                ArtistName = hasTrackMeta ? trackInfo.ArtistName : null,
                AlbumTitle = hasTrackMeta ? trackInfo.AlbumTitle : null,
                AlbumId = hasTrackMeta ? trackInfo.AlbumId : null,
                AlbumArtworkUrl = hasTrackMeta ? trackInfo.AlbumArtworkUrl : null,
                TrackNumber = hasTrackMeta ? trackInfo.TrackNumber : (int?)null,
                DurationSeconds = hasTrackMeta ? trackInfo.DurationSeconds : null
            };
        }).ToList();

        return playlist;
    }

    public async Task<Guid> CreatePlaylistAsync(Playlist playlist)
    {
        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();
        return playlist.Id;
    }

    public async Task<Playlist?> GetPlaylistWithItemsAsync(Guid playlistId, Guid profileId)
    {
        return await _context.Playlists.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == playlistId && p.ProfileId == profileId);
    }

    public async Task<bool> IsPlaylistOwnerAsync(Guid playlistId, Guid profileId)
    {
        return await _context.Playlists.AnyAsync(p => p.Id == playlistId && p.ProfileId == profileId);
    }

    public async Task<int> GetMaxItemOrderAsync(Guid playlistId)
    {
        var max = await _context.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (int?)i.Order);
        return max ?? -1;
    }

    public async Task TouchPlaylistAsync(Guid playlistId)
    {
        await _context.Playlists
            .Where(p => p.Id == playlistId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    public async Task AddPlaylistItemAsync(PlaylistItem item)
    {
        _context.PlaylistItems.Add(item);
        await _context.SaveChangesAsync();
    }

    public async Task RemovePlaylistItemAsync(Guid playlistId, Guid profileId, Guid playlistItemId)
    {
        if (await IsPlaylistOwnerAsync(playlistId, profileId))
        {
            await _context.PlaylistItems
                .Where(i => i.PlaylistId == playlistId && i.Id == playlistItemId)
                .ExecuteDeleteAsync();

            await TouchPlaylistAsync(playlistId);
        }
    }

    public async Task UpdatePlaylistAsync(Playlist playlist)
    {
        _context.Playlists.Update(playlist);
        await _context.SaveChangesAsync();
    }

    public async Task UpdatePlaylistDetailsAsync(Guid id, Guid profileId, string name, string? description)
    {
        await _context.Playlists
            .Where(p => p.Id == id && p.ProfileId == profileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, name)
                .SetProperty(x => x.Description, description)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
    }

    public async Task<List<Guid>> GetPlaylistMediaIdsAsync(Guid playlistId, Guid profileId)
    {
        return await _context.PlaylistItems
            .Where(i => i.PlaylistId == playlistId && i.Playlist.ProfileId == profileId)
            .Select(i => i.MediaItemId)
            .Distinct()
            .ToListAsync();
    }

    public async Task MarkItemsUnplayedAsync(Guid profileId, List<Guid> mediaIds)
    {
        await _context.UserMediaStates
            .Where(s => s.ProfileId == profileId && mediaIds.Contains(s.MediaItemId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsPlayed, false)
                .SetProperty(x => x.ResumePositionSeconds, 0));
    }

    public async Task DeletePlaylistAsync(Guid playlistId, Guid profileId)
    {
        await _context.Playlists
            .Where(p => p.Id == playlistId && p.ProfileId == profileId)
            .ExecuteDeleteAsync();
    }

    public async Task<List<Guid>> GetPlaylistsContainingItemAsync(Guid profileId, Guid mediaItemId)
    {
        return await _context.PlaylistItems
            .Where(i => i.Playlist.ProfileId == profileId && i.MediaItemId == mediaItemId)
            .Select(i => i.PlaylistId)
            .Distinct()
            .ToListAsync();
    }

    public async Task RemoveMediaFromPlaylistAsync(Guid playlistId, Guid profileId, Guid mediaItemId)
    {
        if (await IsPlaylistOwnerAsync(playlistId, profileId))
        {
            await _context.PlaylistItems
                .Where(i => i.PlaylistId == playlistId && i.MediaItemId == mediaItemId)
                .ExecuteDeleteAsync();

            await TouchPlaylistAsync(playlistId);
        }
    }
}