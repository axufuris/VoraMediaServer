using Microsoft.EntityFrameworkCore;
using Vora.Application.Media.SmartPlaylists;
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

    // One projection for every list of playlists, so the owner's own list, the
    // Shared tab and the detail header can never disagree about what a playlist
    // holds. Counts and poster mosaics only ever draw on items the viewer may
    // see - a mosaic built from every item would put a restricted film's poster
    // in the header while the film itself was hidden.
    private static IQueryable<PlaylistSummaryVM> Summaries(IQueryable<Playlist> playlists, IQueryable<Guid> visible, Guid viewerProfileId) =>
        playlists.Select(p => new PlaylistSummaryVM
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            MediaType = p.MediaType,
            IsShared = p.IsShared,
            IsOwner = p.ProfileId == viewerProfileId,
            OwnerName = p.Profile.Name,
            ItemCount = p.Items.Count(i => visible.Contains(i.MediaItemId)),
            PosterUrls = p.Items
                .Where(i => visible.Contains(i.MediaItemId))
                .OrderBy(i => i.Order)
                .Select(i => i.MediaItem.PosterUrl
                    ?? (i.MediaItem is Vora.Domain.Entities.Media.Track
                        ? ((Vora.Domain.Entities.Media.Track)i.MediaItem).Album!.ArtworkUrl
                        : null))
                .Where(u => u != null)
                .Select(u => u!)
                .Take(4)
                .ToList(),
            BackdropUrls = p.Items
                .Where(i => visible.Contains(i.MediaItemId))
                .OrderBy(i => i.Order)
                .Select(i => i.MediaItem.BackgroundUrl)
                .Where(u => u != null)
                .Select(u => u!)
                .Take(4)
                .ToList()
        });

    public async Task<List<PlaylistSummaryVM>> GetPlaylistsAsync(Guid profileId, PlaylistAccessFilter access)
    {
        var visible = PlaylistVisibility.VisibleMediaIds(_context, access);
        return await Summaries(
                _context.Playlists.AsNoTracking().Where(p => p.ProfileId == profileId).OrderBy(p => p.Name),
                visible,
                profileId)
            .ToListAsync();
    }

    // Other people's shared playlists, newest share first. One the viewer can see
    // nothing in is left out entirely rather than listed as empty: a child has no
    // use for an adult's "Horror Marathon", and listing it would show them the
    // title of something their controls exist to keep from them.
    public async Task<List<PlaylistSummaryVM>> GetSharedByOthersAsync(Guid viewerProfileId, PlaylistAccessFilter access)
    {
        var visible = PlaylistVisibility.VisibleMediaIds(_context, access);
        var shared = await Summaries(
                _context.Playlists
                    .AsNoTracking()
                    .Where(p => p.IsShared && p.ProfileId != viewerProfileId)
                    .OrderByDescending(p => p.SharedAt),
                visible,
                viewerProfileId)
            .ToListAsync();

        return shared.Where(p => p.ItemCount > 0).ToList();
    }

    // Owner only. Unsharing clears SharedAt, so re-sharing later puts it back at
    // the top of everyone's list, where something newly shared belongs.
    public async Task<bool> SetSharedAsync(Guid id, Guid ownerProfileId, bool isShared)
    {
        var playlist = await _context.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.ProfileId == ownerProfileId);
        if (playlist == null) return false;

        if (playlist.IsShared != isShared)
        {
            playlist.IsShared = isShared;
            playlist.SharedAt = isShared ? DateTime.UtcNow : null;
            await _context.SaveChangesAsync();
        }

        return true;
    }

    // A fork the viewer owns outright. It starts UNSHARED, so saving someone's
    // playlist does not put a second copy of it into everyone's Shared tab, and
    // it holds only the items the viewer may see - copying the rest would slip a
    // restricted title into a restricted profile's own playlist, where it would
    // then sit in their library as if they had chosen it.
    public async Task<Guid?> CopyPlaylistAsync(Guid sourceId, Guid viewerProfileId, PlaylistAccessFilter access)
    {
        var source = await _context.Playlists
            .AsNoTracking()
            .Where(p => p.Id == sourceId && (p.ProfileId == viewerProfileId || p.IsShared))
            .Select(p => new { p.Name, p.Description, p.MediaType })
            .FirstOrDefaultAsync();

        if (source == null) return null;

        var visible = PlaylistVisibility.VisibleMediaIds(_context, access);
        var mediaIds = await _context.PlaylistItems
            .AsNoTracking()
            .Where(i => i.PlaylistId == sourceId && visible.Contains(i.MediaItemId))
            .OrderBy(i => i.Order)
            .Select(i => i.MediaItemId)
            .ToListAsync();

        var copy = new Playlist
        {
            ProfileId = viewerProfileId,
            Name = source.Name,
            Description = source.Description,
            MediaType = source.MediaType,
            IsShared = false,
            Items = mediaIds.Select((mediaId, index) => new PlaylistItem { MediaItemId = mediaId, Order = index + 1 }).ToList()
        };

        _context.Playlists.Add(copy);
        await _context.SaveChangesAsync();
        return copy.Id;
    }

    // Readable by the owner, or by anyone once it is shared - and by nobody once
    // it has been deleted or unshared, which is what a viewer who had it open
    // sees as "no longer available". Writes are not affected: every write path
    // still matches on the owner alone.
    public async Task<PlaylistDetailsVM?> GetPlaylistDetailsAsync(Guid id, Guid viewerProfileId, PlaylistAccessFilter access)
    {
        var profileId = viewerProfileId;
        var visible = PlaylistVisibility.VisibleMediaIds(_context, access);

        var summary = await Summaries(
                _context.Playlists.AsNoTracking().Where(p => p.Id == id && (p.ProfileId == viewerProfileId || p.IsShared)),
                visible,
                viewerProfileId)
            .FirstOrDefaultAsync();

        if (summary == null) return null;

        var playlist = new PlaylistDetailsVM
        {
            Id = summary.Id,
            Name = summary.Name,
            Description = summary.Description,
            MediaType = summary.MediaType,
            ItemCount = summary.ItemCount,
            PosterUrls = summary.PosterUrls,
            BackdropUrls = summary.BackdropUrls,
            IsShared = summary.IsShared,
            IsOwner = summary.IsOwner,
            OwnerName = summary.OwnerName
        };

        var rawItems = await _context.PlaylistItems
            .AsNoTracking()
            .Include(i => i.MediaItem)
                .ThenInclude(m => m.Analysis)
            .Where(i => i.PlaylistId == id && visible.Contains(i.MediaItemId))
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