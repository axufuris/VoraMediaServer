using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Search.ViewModels;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MusicRepository : IMusicRepository
{
    private readonly VoraDbContext _context;

    public MusicRepository(VoraDbContext context)
    {
        _context = context;
    }

    public Task<Artist?> GetArtistByNameAsync(Guid libraryId, string name) =>
        _context.Artists.FirstOrDefaultAsync(a => a.LibraryId == libraryId && a.Name == name);

    public Task<Album?> GetAlbumByTitleAsync(Guid artistId, string title) =>
        _context.Albums.FirstOrDefaultAsync(a => a.ArtistId == artistId && a.Title == title);

    public Task<Track?> GetTrackByAlbumAndNumberAsync(Guid albumId, int trackNumber, int? discNumber) =>
        _context.Tracks.FirstOrDefaultAsync(t =>
            t.AlbumId == albumId
            && t.TrackNumber == trackNumber
            && t.DiscNumber == discNumber);

    public async Task AddArtistAsync(Artist artist)
    {
        await _context.Artists.AddAsync(artist);
        await _context.SaveChangesAsync();
    }

    public async Task AddAlbumAsync(Album album)
    {
        await _context.Albums.AddAsync(album);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateArtistAsync(Artist artist)
    {
        _context.Artists.Update(artist);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAlbumAsync(Album album)
    {
        _context.Albums.Update(album);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Artist>> GetArtistsAsync(Guid? libraryId, MusicAccessFilter access, int? limit = null)
    {
        var query = _context.Artists.AsNoTracking().AsQueryable();
        if (libraryId.HasValue)
        {
            var id = libraryId.Value;
            query = query.Where(a => a.LibraryId == id);
        }
        query = ApplyLibraryFilter(query, access);

        var allowedTracks = _context.Tracks.AsNoTracking();
        allowedTracks = ApplyRatingFilterToTracks(allowedTracks, access);
        var anyTracksByArtist = allowedTracks
            .Where(t => t.AlbumId != null)
            .Join(_context.Albums.AsNoTracking(), t => t.AlbumId, a => (Guid?)a.Id, (t, a) => a.ArtistId);

        query = query.Where(a => anyTracksByArtist.Contains(a.Id));

        var ordered = query.OrderBy(a => a.SortName ?? a.Name);
        if (limit.HasValue)
        {
            return await ordered.Take(limit.Value).ToListAsync();
        }
        return await ordered.ToListAsync();
    }

    public async Task<List<Album>> GetAlbumsForArtistAsync(Guid artistId, MusicAccessFilter access)
    {
        var query = _context.Albums.AsNoTracking().Where(a => a.ArtistId == artistId);
        query = ApplyLibraryFilter(query, access);

        var allowedTracks = ApplyRatingFilterToTracks(_context.Tracks.AsNoTracking(), access)
            .Where(t => t.AlbumId != null)
            .Select(t => t.AlbumId!.Value);

        query = query.Where(a => allowedTracks.Contains(a.Id));

        return await query
            .OrderBy(a => a.Year)
            .ThenBy(a => a.SortTitle ?? a.Title)
            .ToListAsync();
    }

    public async Task<Artist?> GetArtistByIdAsync(Guid artistId, MusicAccessFilter access)
    {
        var query = _context.Artists.AsNoTracking().Where(a => a.Id == artistId);
        query = ApplyLibraryFilter(query, access);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<Album?> GetAlbumByIdAsync(Guid albumId, MusicAccessFilter access)
    {
        IQueryable<Album> query = _context.Albums.AsNoTracking().Include(a => a.Artist).Where(a => a.Id == albumId);
        query = ApplyLibraryFilter(query, access);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<List<Track>> GetTracksForAlbumAsync(Guid albumId, MusicAccessFilter access)
    {
        var query = _context.Tracks.AsNoTracking().Where(t => t.AlbumId == albumId);
        query = ApplyLibraryFilter(query, access);
        query = ApplyRatingFilterToTracks(query, access);

        return await query
            .OrderBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .ToListAsync();
    }

    public async Task<List<Track>> GetTracksForArtistAsync(Guid artistId, MusicAccessFilter access)
    {
        var albumIds = await _context.Albums
            .AsNoTracking()
            .Where(a => a.ArtistId == artistId)
            .Select(a => a.Id)
            .ToListAsync();

        if (albumIds.Count == 0) return new List<Track>();

        IQueryable<Track> query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => t.AlbumId != null && albumIds.Contains(t.AlbumId.Value));
        query = ApplyLibraryFilter(query, access);
        query = ApplyRatingFilterToTracks(query, access);

        return await query
            .OrderBy(t => t.Album!.Year)
            .ThenBy(t => t.Album!.SortTitle ?? t.Album!.Title)
            .ThenBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .ToListAsync();
    }

    public async Task<Track?> GetTrackByIdAsync(Guid trackId, MusicAccessFilter access)
    {
        var query = _context.Tracks.AsNoTracking().Where(t => t.Id == trackId);
        query = ApplyLibraryFilter(query, access);
        query = ApplyRatingFilterToTracks(query, access);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<string?> GetTrackFilePathAsync(Guid trackId, MusicAccessFilter access)
    {
        var trackQuery = _context.Tracks.AsNoTracking().Where(t => t.Id == trackId);
        trackQuery = ApplyLibraryFilter(trackQuery, access);
        trackQuery = ApplyRatingFilterToTracks(trackQuery, access);

        var allowed = await trackQuery.Select(t => t.Id).FirstOrDefaultAsync();
        if (allowed == Guid.Empty) return null;

        return await _context.MediaParts
            .AsNoTracking()
            .Where(p => p.MediaItemId == trackId)
            .Select(p => p.FilePath)
            .FirstOrDefaultAsync();
    }

    public Task<Artist?> GetArtistForUpdateAsync(Guid artistId) =>
        _context.Artists.FirstOrDefaultAsync(a => a.Id == artistId);

    public Task<Album?> GetAlbumForUpdateAsync(Guid albumId) =>
        _context.Albums.Include(a => a.Artist).FirstOrDefaultAsync(a => a.Id == albumId);

    public Task<Track?> GetTrackForUpdateAsync(Guid trackId) =>
        _context.Tracks.FirstOrDefaultAsync(t => t.Id == trackId);

    public async Task UpdateTrackAsync(Track track)
    {
        _context.Tracks.Update(track);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> SetTrackLikedAsync(Guid profileId, Guid trackId, bool liked)
    {
        var existing = await _context.TrackLikes.FirstOrDefaultAsync(l => l.ProfileId == profileId && l.TrackId == trackId);
        if (liked)
        {
            if (existing != null) return false;
            await _context.TrackLikes.AddAsync(new TrackLike { ProfileId = profileId, TrackId = trackId });
            await _context.SaveChangesAsync();
            return true;
        }

        if (existing == null) return false;
        _context.TrackLikes.Remove(existing);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<HashSet<Guid>> GetLikedTrackIdsAsync(Guid profileId, IEnumerable<Guid> trackIds)
    {
        var ids = trackIds.ToList();
        if (ids.Count == 0) return new HashSet<Guid>();

        var liked = await _context.TrackLikes
            .AsNoTracking()
            .Where(l => l.ProfileId == profileId && ids.Contains(l.TrackId))
            .Select(l => l.TrackId)
            .ToListAsync();
        return liked.ToHashSet();
    }

    public async Task<List<Track>> GetLikedTracksAsync(Guid profileId, MusicAccessFilter access)
    {
        var likedJoin = _context.TrackLikes
            .AsNoTracking()
            .Where(l => l.ProfileId == profileId);

        IQueryable<Track> query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .ThenInclude(a => a!.Artist);
        query = ApplyLibraryFilter(query, access);
        query = ApplyRatingFilterToTracks(query, access);

        var ordered = from track in query
                      join like in likedJoin on track.Id equals like.TrackId
                      orderby like.LikedAt descending
                      select track;

        return await ordered.ToListAsync();
    }

    public Task<int> GetLikedTrackCountAsync(Guid profileId) =>
        _context.TrackLikes.CountAsync(l => l.ProfileId == profileId);

    public async Task RecordPlayAsync(Guid profileId, Guid trackId, int durationListenedSeconds, bool completed)
    {
        var entry = new TrackPlayHistory
        {
            ProfileId = profileId,
            TrackId = trackId,
            PlayedAt = DateTime.UtcNow,
            DurationListenedSeconds = Math.Max(0, durationListenedSeconds),
            Completed = completed
        };
        await _context.TrackPlayHistory.AddAsync(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Track>> GetRecentlyPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var perTrackLatest = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(h => h.ProfileId == profileId)
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, LastPlayed = g.Max(h => h.PlayedAt) });

        IQueryable<Track> tracks = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .ThenInclude(a => a!.Artist);
        tracks = ApplyLibraryFilter(tracks, access);
        tracks = ApplyRatingFilterToTracks(tracks, access);

        var query = from track in tracks
                    join recent in perTrackLatest on track.Id equals recent.TrackId
                    orderby recent.LastPlayed descending
                    select track;

        return await query.Take(Math.Max(1, limit)).ToListAsync();
    }

    public async Task<List<Track>> GetTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var perTrackCount = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(h => h.ProfileId == profileId)
            .GroupBy(h => h.TrackId)
            .Select(g => new { TrackId = g.Key, Plays = g.Count(), LastPlayed = g.Max(h => h.PlayedAt) });

        IQueryable<Track> tracks = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .ThenInclude(a => a!.Artist);
        tracks = ApplyLibraryFilter(tracks, access);
        tracks = ApplyRatingFilterToTracks(tracks, access);

        var query = from track in tracks
                    join stat in perTrackCount on track.Id equals stat.TrackId
                    orderby stat.Plays descending, stat.LastPlayed descending
                    select track;

        return await query.Take(Math.Max(1, limit)).ToListAsync();
    }

    public async Task<List<Album>> GetRecentlyAddedAlbumsAsync(MusicAccessFilter access, int limit)
    {
        IQueryable<Album> query = _context.Albums.AsNoTracking().Include(a => a.Artist);
        query = ApplyLibraryFilter(query, access);

        var allowedTracks = ApplyRatingFilterToTracks(_context.Tracks.AsNoTracking(), access)
            .Where(t => t.AlbumId != null)
            .Select(t => t.AlbumId!.Value);

        query = query.Where(a => allowedTracks.Contains(a.Id));

        return await query
            .OrderByDescending(a => a.AddedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync();
    }

    public async Task<List<Artist>> GetTopPlayedArtistsAsync(Guid profileId, MusicAccessFilter access, int limit)
    {
        var artistPlays = from history in _context.TrackPlayHistory.AsNoTracking()
                          where history.ProfileId == profileId
                          join track in _context.Tracks.AsNoTracking() on history.TrackId equals track.Id
                          join album in _context.Albums.AsNoTracking() on track.AlbumId equals album.Id
                          group new { history, album } by album.ArtistId into grp
                          select new { ArtistId = grp.Key, Plays = grp.Count(), LastPlayed = grp.Max(g => g.history.PlayedAt) };

        IQueryable<Artist> artists = _context.Artists.AsNoTracking();
        artists = ApplyLibraryFilter(artists, access);

        var query = from artist in artists
                    join stat in artistPlays on artist.Id equals stat.ArtistId
                    orderby stat.Plays descending, stat.LastPlayed descending
                    select artist;

        return await query.Take(Math.Max(1, limit)).ToListAsync();
    }

    public async Task<List<MusicSearchResultVM>> SearchAsync(string query, MusicAccessFilter access, int limit)
    {
        var searchLower = query.ToLower();
        var perTypeLimit = Math.Max(1, limit);

        var artistsQuery = ApplyLibraryFilter(_context.Artists.AsNoTracking(), access)
            .Where(a => a.Name.ToLower().Contains(searchLower));

        var artistResults = await artistsQuery
            .OrderBy(a => a.SortName ?? a.Name)
            .Take(perTypeLimit)
            .Select(a => new MusicSearchResultVM
            {
                Id = a.Id,
                Type = "Artist",
                Title = a.Name,
                Subtitle = null,
                ArtworkUrl = a.ArtworkUrl,
                ArtistId = a.Id,
                AlbumId = null
            })
            .ToListAsync();

        var albumsQuery = ApplyLibraryFilter(_context.Albums.AsNoTracking().Include(a => a.Artist), access)
            .Where(a => a.Title.ToLower().Contains(searchLower));

        var albumResults = await albumsQuery
            .OrderBy(a => a.SortTitle ?? a.Title)
            .Take(perTypeLimit)
            .Select(a => new MusicSearchResultVM
            {
                Id = a.Id,
                Type = "Album",
                Title = a.Title,
                Subtitle = a.Artist != null ? a.Artist.Name : null,
                ArtworkUrl = a.ArtworkUrl,
                ArtistId = a.ArtistId,
                AlbumId = a.Id
            })
            .ToListAsync();

        var tracksQuery = _context.Tracks.AsNoTracking().Include(t => t.Album).ThenInclude(a => a!.Artist).AsQueryable();
        tracksQuery = ApplyLibraryFilter(tracksQuery, access);
        tracksQuery = ApplyRatingFilterToTracks(tracksQuery, access);
        tracksQuery = tracksQuery.Where(t => t.Title.ToLower().Contains(searchLower));

        var trackResults = await tracksQuery
            .OrderBy(t => t.SortTitle ?? t.Title)
            .Take(perTypeLimit)
            .Select(t => new MusicSearchResultVM
            {
                Id = t.Id,
                Type = "Track",
                Title = t.Title,
                Subtitle = t.Album != null && t.Album.Artist != null
                    ? t.Album.Artist.Name + " — " + t.Album.Title
                    : (t.Album != null ? t.Album.Title : null),
                ArtworkUrl = t.Album != null ? t.Album.ArtworkUrl : null,
                ArtistId = t.Album != null ? t.Album.ArtistId : (Guid?)null,
                AlbumId = t.AlbumId
            })
            .ToListAsync();

        var combined = new List<MusicSearchResultVM>(artistResults.Count + albumResults.Count + trackResults.Count);
        combined.AddRange(artistResults);
        combined.AddRange(albumResults);
        combined.AddRange(trackResults);
        return combined;
    }

    public async Task<List<GenreSummary>> GetGenreSummariesAsync(MusicAccessFilter access)
    {
        var albumQuery = _context.Albums.AsNoTracking().Where(a => a.Genre != null && a.Genre != string.Empty);
        albumQuery = ApplyLibraryFilter(albumQuery, access);

        var albums = await albumQuery
            .Select(a => new { a.Id, a.Genre, a.ArtistId, a.ArtworkUrl })
            .ToListAsync();

        var trackQuery = _context.Tracks.AsNoTracking().Include(t => t.Album).Where(t => t.Album != null && t.Album.Genre != null && t.Album.Genre != string.Empty);
        trackQuery = ApplyLibraryFilter(trackQuery, access);
        trackQuery = ApplyRatingFilterToTracks(trackQuery, access);

        var trackCounts = await trackQuery
            .GroupBy(t => t.Album!.Genre)
            .Select(g => new { Genre = g.Key, Count = g.Count() })
            .ToListAsync();
        var trackCountByGenre = trackCounts.ToDictionary(t => t.Genre!, t => t.Count, StringComparer.OrdinalIgnoreCase);

        return albums
            .GroupBy(a => a.Genre!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GenreSummary
            {
                Name = g.Key,
                AlbumCount = g.Select(a => a.Id).Distinct().Count(),
                ArtistCount = g.Select(a => a.ArtistId).Distinct().Count(),
                TrackCount = trackCountByGenre.TryGetValue(g.Key, out var c) ? c : 0,
                SampleArtworkUrl = g.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.ArtworkUrl))?.ArtworkUrl
            })
            .OrderByDescending(g => g.TrackCount)
            .ToList();
    }

    public async Task<(List<AdminPlayHistoryRow> Rows, int Total)> GetAdminPlayHistoryAsync(Guid? profileId, DateTime? from, DateTime? to, string? search, int page, int pageSize)
    {
        var query = _context.TrackPlayHistory.AsNoTracking().AsQueryable();
        if (profileId.HasValue) query = query.Where(p => p.ProfileId == profileId.Value);
        if (from.HasValue) query = query.Where(p => p.PlayedAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PlayedAt < to.Value);

        var joined = query
            .Join(_context.Tracks.AsNoTracking(), p => p.TrackId, t => t.Id, (p, t) => new { p.Id, p.ProfileId, p.PlayedAt, p.DurationListenedSeconds, p.Completed, Track = t })
            .GroupJoin(_context.UserProfiles.AsNoTracking(), x => x.ProfileId, up => up.Id, (x, profs) => new { x, profs })
            .SelectMany(x => x.profs.DefaultIfEmpty(), (x, up) => new { x.x, ProfileName = up != null ? up.Name : "(Unknown)" })
            .GroupJoin(_context.Albums.AsNoTracking(), x => x.x.Track.AlbumId, a => (Guid?)a.Id, (x, albums) => new { x.x, x.ProfileName, albums })
            .SelectMany(x => x.albums.DefaultIfEmpty(), (x, a) => new { x.x, x.ProfileName, Album = a })
            .GroupJoin(_context.Artists.AsNoTracking(), x => x.Album != null ? (Guid?)x.Album.ArtistId : null, ar => (Guid?)ar.Id, (x, artists) => new { x.x, x.ProfileName, x.Album, artists })
            .SelectMany(x => x.artists.DefaultIfEmpty(), (x, ar) => new { x.x, x.ProfileName, x.Album, Artist = ar });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            joined = joined.Where(j =>
                j.x.Track.Title.ToLower().Contains(s)
                || (j.Album != null && j.Album.Title.ToLower().Contains(s))
                || (j.Artist != null && j.Artist.Name.ToLower().Contains(s))
                || (j.x.Track.Artist != null && j.x.Track.Artist.ToLower().Contains(s))
                || j.ProfileName.ToLower().Contains(s));
        }

        var total = await joined.CountAsync();

        var rows = await joined
            .OrderByDescending(j => j.x.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new AdminPlayHistoryRow
            {
                Id = j.x.Id,
                ProfileId = j.x.ProfileId,
                ProfileName = j.ProfileName,
                TrackId = j.x.Track.Id,
                TrackTitle = j.x.Track.Title,
                Artist = j.x.Track.Artist ?? (j.Artist != null ? j.Artist.Name : null),
                AlbumTitle = j.Album != null ? j.Album.Title : null,
                AlbumArtworkUrl = j.Album != null ? j.Album.ArtworkUrl : null,
                PlayedAt = j.x.PlayedAt,
                DurationListenedSeconds = j.x.DurationListenedSeconds,
                Completed = j.x.Completed
            })
            .ToListAsync();

        return (rows, total);
    }

    public async Task<List<AdminTopTrackRow>> GetServerTopTracksAsync(DateTime? from, DateTime? to, int limit)
    {
        var query = _context.TrackPlayHistory.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(p => p.PlayedAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PlayedAt < to.Value);

        var counts = await query
            .GroupBy(p => p.TrackId)
            .Select(g => new { TrackId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        var trackIds = counts.Select(c => c.TrackId).ToList();
        var tracks = await _context.Tracks.AsNoTracking()
            .Include(t => t.Album)!.ThenInclude(a => a!.Artist)
            .Where(t => trackIds.Contains(t.Id))
            .ToListAsync();

        var byId = tracks.ToDictionary(t => t.Id);
        return counts
            .Where(c => byId.ContainsKey(c.TrackId))
            .Select(c =>
            {
                var t = byId[c.TrackId];
                return new AdminTopTrackRow
                {
                    TrackId = t.Id,
                    TrackTitle = t.Title,
                    Artist = t.Artist ?? t.Album?.Artist?.Name,
                    AlbumTitle = t.Album?.Title,
                    AlbumArtworkUrl = t.Album?.ArtworkUrl,
                    PlayCount = c.Count
                };
            })
            .ToList();
    }

    public async Task<List<AdminTopArtistRow>> GetServerTopArtistsAsync(DateTime? from, DateTime? to, int limit)
    {
        var query = _context.TrackPlayHistory.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(p => p.PlayedAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PlayedAt < to.Value);

        var counts = await query
            .Join(_context.Tracks.AsNoTracking(), p => p.TrackId, t => t.Id, (p, t) => new { p, t })
            .Where(x => x.t.AlbumId != null)
            .Join(_context.Albums.AsNoTracking(), x => x.t.AlbumId, a => (Guid?)a.Id, (x, a) => new { x.p, ArtistId = a.ArtistId })
            .GroupBy(x => x.ArtistId)
            .Select(g => new { ArtistId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        var ids = counts.Select(c => c.ArtistId).ToList();
        var artists = await _context.Artists.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a);

        return counts
            .Where(c => artists.ContainsKey(c.ArtistId))
            .Select(c => new AdminTopArtistRow
            {
                ArtistId = c.ArtistId,
                ArtistName = artists[c.ArtistId].Name,
                ArtworkUrl = artists[c.ArtistId].ArtworkUrl,
                PlayCount = c.Count
            })
            .ToList();
    }

    public async Task<List<AdminProfilePlayCount>> GetPlaysPerProfileAsync(DateTime? from, DateTime? to)
    {
        var query = _context.TrackPlayHistory.AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(p => p.PlayedAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PlayedAt < to.Value);

        var counts = await query
            .GroupBy(p => p.ProfileId)
            .Select(g => new { ProfileId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        var profileIds = counts.Select(c => c.ProfileId).ToList();
        var profiles = await _context.UserProfiles.AsNoTracking()
            .Where(p => profileIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        return counts.Select(c => new AdminProfilePlayCount
        {
            ProfileId = c.ProfileId,
            ProfileName = profiles.TryGetValue(c.ProfileId, out var n) ? n : "(Unknown)",
            PlayCount = c.Count
        }).ToList();
    }

    public async Task<GenreContent> GetGenreContentAsync(string genre, MusicAccessFilter access)
    {
        var albumQuery = _context.Albums.AsNoTracking().Include(a => a.Artist).Where(a => a.Genre != null && a.Genre.ToLower() == genre.ToLower());
        albumQuery = ApplyLibraryFilter(albumQuery, access);
        var albums = await albumQuery.OrderByDescending(a => a.AddedAt).Take(60).ToListAsync();

        var artistIds = albums.Select(a => a.ArtistId).Distinct().ToList();
        var artists = await _context.Artists.AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Take(40)
            .ToListAsync();

        var trackQuery = _context.Tracks.AsNoTracking().Include(t => t.Album)
            .Where(t => t.Album != null && t.Album.Genre != null && t.Album.Genre.ToLower() == genre.ToLower());
        trackQuery = ApplyLibraryFilter(trackQuery, access);
        trackQuery = ApplyRatingFilterToTracks(trackQuery, access);
        var tracks = await trackQuery.Take(50).ToListAsync();

        return new GenreContent
        {
            Name = genre,
            Artists = artists,
            Albums = albums,
            Tracks = tracks
        };
    }

    private static IQueryable<Artist> ApplyLibraryFilter(IQueryable<Artist> query, MusicAccessFilter access)
    {
        if (access.HasAllLibraryAccess) return query;
        var allowed = access.AllowedLibraryIds;
        return query.Where(a => allowed.Contains(a.LibraryId));
    }

    private static IQueryable<Album> ApplyLibraryFilter(IQueryable<Album> query, MusicAccessFilter access)
    {
        if (access.HasAllLibraryAccess) return query;
        var allowed = access.AllowedLibraryIds;
        return query.Where(a => allowed.Contains(a.LibraryId));
    }

    private static IQueryable<Track> ApplyLibraryFilter(IQueryable<Track> query, MusicAccessFilter access)
    {
        if (access.HasAllLibraryAccess) return query;
        var allowed = access.AllowedLibraryIds;
        return query.Where(t => allowed.Contains(t.LibraryId));
    }

    private static IQueryable<Track> ApplyRatingFilterToTracks(IQueryable<Track> query, MusicAccessFilter access)
    {
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        if (!access.HasAllRatings)
        {
            var allowed = access.AllowedRatings;
            query = query.Where(t => t.ContentRating == null || allowed.Contains(t.ContentRating));
        }

        return query;
    }

    public async Task<Dictionary<Guid, decimal>> GetAlbumRatingsAsync(Guid profileId, IEnumerable<Guid> albumIds)
    {
        var idList = albumIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, decimal>();

        return await _context.UserAlbumRatings
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && idList.Contains(r.AlbumId))
            .ToDictionaryAsync(r => r.AlbumId, r => r.Rating);
    }

    public async Task<Dictionary<Guid, decimal>> GetArtistRatingsAsync(Guid profileId, IEnumerable<Guid> artistIds)
    {
        var idList = artistIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, decimal>();

        return await _context.UserArtistRatings
            .AsNoTracking()
            .Where(r => r.ProfileId == profileId && idList.Contains(r.ArtistId))
            .ToDictionaryAsync(r => r.ArtistId, r => r.Rating);
    }

    public async Task<SetMusicRatingResult> SetAlbumRatingAsync(Guid profileId, Guid albumId, decimal? rating, bool isAdmin)
    {
        var album = await _context.Albums.FirstOrDefaultAsync(a => a.Id == albumId);
        if (album == null) return new SetMusicRatingResult { Found = false, ServerAdminRatingChanged = false };

        var existing = await _context.UserAlbumRatings.FirstOrDefaultAsync(r => r.ProfileId == profileId && r.AlbumId == albumId);

        if (rating.HasValue)
        {
            if (existing == null)
            {
                _context.UserAlbumRatings.Add(new Vora.Domain.Entities.Users.UserAlbumRating
                {
                    ProfileId = profileId,
                    AlbumId = albumId,
                    Rating = rating.Value,
                    RatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Rating = rating.Value;
                existing.RatedAt = DateTime.UtcNow;
            }
        }
        else if (existing != null)
        {
            _context.UserAlbumRatings.Remove(existing);
        }

        bool serverAdminChanged = false;
        if (isAdmin && album.ServerAdminRating != rating)
        {
            album.ServerAdminRating = rating;
            serverAdminChanged = true;
        }

        await _context.SaveChangesAsync();
        return new SetMusicRatingResult { Found = true, ServerAdminRatingChanged = serverAdminChanged };
    }

    public async Task<SetMusicRatingResult> SetArtistRatingAsync(Guid profileId, Guid artistId, decimal? rating, bool isAdmin)
    {
        var artist = await _context.Artists.FirstOrDefaultAsync(a => a.Id == artistId);
        if (artist == null) return new SetMusicRatingResult { Found = false, ServerAdminRatingChanged = false };

        var existing = await _context.UserArtistRatings.FirstOrDefaultAsync(r => r.ProfileId == profileId && r.ArtistId == artistId);

        if (rating.HasValue)
        {
            if (existing == null)
            {
                _context.UserArtistRatings.Add(new Vora.Domain.Entities.Users.UserArtistRating
                {
                    ProfileId = profileId,
                    ArtistId = artistId,
                    Rating = rating.Value,
                    RatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Rating = rating.Value;
                existing.RatedAt = DateTime.UtcNow;
            }
        }
        else if (existing != null)
        {
            _context.UserArtistRatings.Remove(existing);
        }

        bool serverAdminChanged = false;
        if (isAdmin && artist.ServerAdminRating != rating)
        {
            artist.ServerAdminRating = rating;
            serverAdminChanged = true;
        }

        await _context.SaveChangesAsync();
        return new SetMusicRatingResult { Found = true, ServerAdminRatingChanged = serverAdminChanged };
    }
}
