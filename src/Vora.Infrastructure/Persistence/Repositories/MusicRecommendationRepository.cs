using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MusicRecommendationRepository : IMusicRecommendationRepository
{
    private readonly VoraDbContext _context;

    public MusicRecommendationRepository(VoraDbContext context)
    {
        _context = context;
    }

    public async Task<List<GeneratedMix>> GetMixesForProfileAsync(Guid profileId, GeneratedMixKind kind)
    {
        return await _context.GeneratedMixes
            .AsNoTracking()
            .Where(m => m.ProfileId == profileId && m.Kind == kind)
            .OrderBy(m => m.Slot)
            .ToListAsync();
    }

    public async Task<GeneratedMix?> GetMixByIdAsync(Guid mixId, Guid profileId)
    {
        return await _context.GeneratedMixes
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mixId && m.ProfileId == profileId);
    }

    public async Task SaveMixAsync(GeneratedMix mix)
    {
        var existing = await _context.GeneratedMixes
            .FirstOrDefaultAsync(m => m.ProfileId == mix.ProfileId && m.Kind == mix.Kind && m.Slot == mix.Slot);
        if (existing == null)
        {
            await _context.GeneratedMixes.AddAsync(mix);
        }
        else
        {
            existing.Name = mix.Name;
            existing.DescriptionTag = mix.DescriptionTag;
            existing.ArtworkUrl = mix.ArtworkUrl;
            existing.TrackOrder = mix.TrackOrder;
            existing.LastDriftAt = DateTime.UtcNow;
            _context.GeneratedMixes.Update(existing);
        }
        await _context.SaveChangesAsync();
    }

    public async Task DeleteMixesForProfileAsync(Guid profileId, GeneratedMixKind kind)
    {
        var mixes = await _context.GeneratedMixes
            .Where(m => m.ProfileId == profileId && m.Kind == kind)
            .ToListAsync();
        if (mixes.Count > 0)
        {
            _context.GeneratedMixes.RemoveRange(mixes);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<Guid>> GetProfileIdsWithRecentActivityAsync(int withinDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-withinDays);
        return await _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.PlayedAt >= cutoff)
            .Select(p => p.ProfileId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<ArtistPlayScore>> GetTopArtistsForProfileAsync(Guid profileId, MusicAccessFilter access, int withinDays, int limit)
    {
        var cutoff = DateTime.UtcNow.AddDays(-withinDays);
        var now = DateTime.UtcNow;

        var playsQuery = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.PlayedAt >= cutoff)
            .Join(_context.Tracks, p => p.TrackId, t => t.Id, (p, t) => new { Play = p, Track = t })
            .Where(pt => pt.Track.AlbumId != null)
            .Join(_context.Albums, pt => pt.Track.AlbumId, a => (Guid?)a.Id, (pt, a) => new { pt.Play, pt.Track, Album = a });

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            playsQuery = playsQuery.Where(x => allowed.Contains(x.Album.LibraryId));
        }

        var rawPlays = await playsQuery
            .Select(x => new { x.Album.ArtistId, x.Play.PlayedAt })
            .ToListAsync();

        var scored = rawPlays
            .GroupBy(x => x.ArtistId)
            .Select(g => new
            {
                ArtistId = g.Key,
                Score = g.Sum(p => Math.Exp(-(now - p.PlayedAt).TotalDays / 30.0))
            })
            .OrderByDescending(s => s.Score)
            .Take(limit)
            .ToList();

        if (scored.Count == 0) return new List<ArtistPlayScore>();

        var artistIds = scored.Select(s => s.ArtistId).ToList();
        var artistMap = await _context.Artists
            .AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Name);

        return scored
            .Where(s => artistMap.ContainsKey(s.ArtistId))
            .Select(s => new ArtistPlayScore
            {
                ArtistId = s.ArtistId,
                ArtistName = artistMap[s.ArtistId],
                Score = s.Score
            })
            .ToList();
    }

    public async Task<List<Track>> GetTopTracksByArtistAsync(Guid artistId, MusicAccessFilter access, Guid? profileId, int limit, int maxPerAlbum)
    {
        var query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => t.AlbumId != null && t.Album!.ArtistId == artistId);

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        var tracks = await query.ToListAsync();
        if (tracks.Count == 0) return tracks;

        Dictionary<Guid, int> playCounts;
        if (profileId.HasValue)
        {
            var trackIds = tracks.Select(t => t.Id).ToList();
            var pid = profileId.Value;
            var counts = await _context.TrackPlayHistory
                .AsNoTracking()
                .Where(p => p.ProfileId == pid && trackIds.Contains(p.TrackId))
                .GroupBy(p => p.TrackId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();
            playCounts = counts.ToDictionary(c => c.Key, c => c.Count);
        }
        else
        {
            playCounts = new Dictionary<Guid, int>();
        }

        return tracks
            .OrderByDescending(t => playCounts.TryGetValue(t.Id, out var c) ? c : 0)
            .ThenBy(t => t.AlbumId)
            .ThenBy(t => t.DiscNumber ?? 1)
            .ThenBy(t => t.TrackNumber)
            .GroupBy(t => t.AlbumId ?? Guid.Empty)
            .SelectMany(g => g.Take(maxPerAlbum))
            .Take(limit)
            .ToList();
    }

    public async Task<Dictionary<Guid, List<string>>> GetGenresForArtistsAsync(IEnumerable<Guid> artistIds)
    {
        var idList = artistIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, List<string>>();

        var rows = await _context.Albums
            .AsNoTracking()
            .Where(a => idList.Contains(a.ArtistId) && a.Genre != null && a.Genre != string.Empty)
            .Select(a => new { a.ArtistId, a.Genre })
            .ToListAsync();

        return rows
            .GroupBy(r => r.ArtistId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Genre!).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<List<Track>> GetTracksByIdsAsync(IEnumerable<Guid> trackIds, MusicAccessFilter access)
    {
        var ids = trackIds.ToList();
        if (ids.Count == 0) return new List<Track>();

        var query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => ids.Contains(t.Id));

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        var fetched = await query.ToListAsync();
        var byId = fetched.ToDictionary(t => t.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<List<Track>> GetLikedTracksByArtistsAsync(Guid profileId, IEnumerable<Guid> artistIds, MusicAccessFilter access, int limit)
    {
        var ids = artistIds.ToList();
        if (ids.Count == 0) return new List<Track>();

        var query = _context.TrackLikes
            .AsNoTracking()
            .Where(l => l.ProfileId == profileId)
            .Join(_context.Tracks, l => l.TrackId, t => t.Id, (l, t) => t)
            .Include(t => t.Album)
            .Where(t => t.AlbumId != null && ids.Contains(t.Album!.ArtistId));

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        return await query.Take(limit).ToListAsync();
    }

    public async Task<List<Track>> GetLikedTracksByGenreAsync(Guid profileId, IEnumerable<string> genres, MusicAccessFilter access, int limit)
    {
        var genreList = genres.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (genreList.Count == 0) return new List<Track>();

        var query = _context.TrackLikes
            .AsNoTracking()
            .Where(l => l.ProfileId == profileId)
            .Join(_context.Tracks, l => l.TrackId, t => t.Id, (l, t) => t)
            .Include(t => t.Album)
            .Where(t => t.Album != null && t.Album.Genre != null && genreList.Contains(t.Album.Genre));

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        return await query.Take(limit).ToListAsync();
    }

    public async Task<List<Track>> GetRecentTopPlayedTracksAsync(Guid profileId, MusicAccessFilter access, int withinDays, int limit)
    {
        var cutoff = DateTime.UtcNow.AddDays(-withinDays);
        var query = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.PlayedAt >= cutoff)
            .GroupBy(p => p.TrackId)
            .Select(g => new { TrackId = g.Key, Plays = g.Count() })
            .OrderByDescending(x => x.Plays)
            .Take(limit * 2);

        var top = await query.ToListAsync();
        var trackIds = top.Select(t => t.TrackId).ToList();
        var tracks = await GetTracksByIdsAsync(trackIds, access);
        var counts = top.ToDictionary(x => x.TrackId, x => x.Plays);
        return tracks
            .OrderByDescending(t => counts.TryGetValue(t.Id, out var c) ? c : 0)
            .Take(limit)
            .ToList();
    }

    public async Task<List<Track>> GetTopTracksByGenresAsync(IEnumerable<string> genres, MusicAccessFilter access, Guid? excludeArtistId, IEnumerable<Guid> excludeTrackIds, int limit)
    {
        var genreList = genres.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (genreList.Count == 0) return new List<Track>();
        var excludeSet = excludeTrackIds.ToHashSet();

        var query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => t.Album != null && t.Album.Genre != null && genreList.Contains(t.Album.Genre));

        if (excludeArtistId.HasValue)
        {
            var exId = excludeArtistId.Value;
            query = query.Where(t => t.Album!.ArtistId != exId);
        }

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        var fetched = await query.Take(limit * 3).ToListAsync();
        return fetched
            .Where(t => !excludeSet.Contains(t.Id))
            .OrderBy(_ => Guid.NewGuid())
            .Take(limit)
            .ToList();
    }

    public async Task<List<Track>> GetTracksByGenreAsync(string genre, MusicAccessFilter access, IEnumerable<Guid> excludeTrackIds, int limit)
    {
        if (string.IsNullOrWhiteSpace(genre)) return new List<Track>();
        var excludeSet = excludeTrackIds.ToHashSet();

        var query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => t.Album != null && t.Album.Genre == genre);

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => allowed.Contains(t.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(t => t.ContentRating != null);
        }

        var fetched = await query.Take(limit * 3).ToListAsync();
        return fetched
            .Where(t => !excludeSet.Contains(t.Id))
            .OrderBy(_ => Guid.NewGuid())
            .Take(limit)
            .ToList();
    }

    public async Task<List<string>> GetAlbumGenresForArtistAsync(Guid artistId)
    {
        return await _context.Albums
            .AsNoTracking()
            .Where(a => a.ArtistId == artistId && a.Genre != null && a.Genre != string.Empty)
            .Select(a => a.Genre!)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Station>> GetStationsForProfileAsync(Guid profileId)
    {
        return await _context.Stations
            .AsNoTracking()
            .Where(s => s.ProfileId == profileId)
            .OrderByDescending(s => s.LastPlayedAt ?? s.CreatedAt)
            .ToListAsync();
    }

    public async Task<Station?> GetStationByIdAsync(Guid stationId, Guid profileId)
    {
        return await _context.Stations
            .FirstOrDefaultAsync(s => s.Id == stationId && s.ProfileId == profileId);
    }

    public async Task AddStationAsync(Station station)
    {
        await _context.Stations.AddAsync(station);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateStationAsync(Station station)
    {
        _context.Stations.Update(station);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteStationAsync(Station station)
    {
        _context.Stations.Remove(station);
        await _context.SaveChangesAsync();
    }

    public async Task<List<YearPlayRow>> GetPlaysForYearAsync(Guid profileId, MusicAccessFilter access, int year)
    {
        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddYears(1);

        var query = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.PlayedAt >= start && p.PlayedAt < end)
            .Join(_context.Tracks, p => p.TrackId, t => t.Id, (p, t) => new { Play = p, Track = t });

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(x => allowed.Contains(x.Track.LibraryId));
        }
        if (access.BlockUnratedContent)
        {
            query = query.Where(x => x.Track.ContentRating != null);
        }

        var withAlbum = query
            .GroupJoin(_context.Albums, x => x.Track.AlbumId, a => (Guid?)a.Id, (x, albums) => new { x.Play, x.Track, Albums = albums })
            .SelectMany(x => x.Albums.DefaultIfEmpty(), (x, a) => new { x.Play, x.Track, Album = a })
            .GroupJoin(_context.Artists, x => x.Album != null ? (Guid?)x.Album.ArtistId : null, ar => (Guid?)ar.Id, (x, artists) => new { x.Play, x.Track, x.Album, Artists = artists })
            .SelectMany(x => x.Artists.DefaultIfEmpty(), (x, ar) => new { x.Play, x.Track, x.Album, Artist = ar });

        return await withAlbum
            .Select(x => new YearPlayRow
            {
                TrackId = x.Track.Id,
                TrackTitle = x.Track.Title,
                TrackArtist = x.Track.Artist,
                AlbumId = x.Track.AlbumId,
                AlbumTitle = x.Album != null ? x.Album.Title : null,
                AlbumArtworkUrl = x.Album != null ? x.Album.ArtworkUrl : null,
                AlbumGenre = x.Album != null ? x.Album.Genre : null,
                ArtistId = x.Artist != null ? (Guid?)x.Artist.Id : null,
                ArtistName = x.Artist != null ? x.Artist.Name : null,
                ArtistArtworkUrl = x.Artist != null ? x.Artist.ArtworkUrl : null,
                PlayedAt = x.Play.PlayedAt,
                DurationListenedSeconds = x.Play.DurationListenedSeconds
            })
            .ToListAsync();
    }

    public async Task<List<int>> GetYearsWithHistoryAsync(Guid profileId)
    {
        return await _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .Select(p => p.PlayedAt.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();
    }

    public async Task<List<ArtistSimilarity>> GetSimilaritiesAsync(Guid artistId)
    {
        return await _context.ArtistSimilarities
            .AsNoTracking()
            .Where(s => s.ArtistId == artistId)
            .OrderByDescending(s => s.Score)
            .ToListAsync();
    }

    public async Task ReplaceSimilaritiesAsync(Guid artistId, IEnumerable<ArtistSimilarity> entries)
    {
        var existing = await _context.ArtistSimilarities.Where(s => s.ArtistId == artistId).ToListAsync();
        if (existing.Count > 0) _context.ArtistSimilarities.RemoveRange(existing);
        var list = entries.ToList();
        if (list.Count > 0) await _context.ArtistSimilarities.AddRangeAsync(list);
        await _context.SaveChangesAsync();
    }

    public async Task<List<ArtistTag>> GetArtistTagsAsync(Guid artistId)
    {
        return await _context.ArtistTags
            .AsNoTracking()
            .Where(t => t.ArtistId == artistId)
            .OrderByDescending(t => t.Weight)
            .ToListAsync();
    }

    public async Task ReplaceArtistTagsAsync(Guid artistId, IEnumerable<ArtistTag> entries)
    {
        var existing = await _context.ArtistTags.Where(t => t.ArtistId == artistId).ToListAsync();
        if (existing.Count > 0) _context.ArtistTags.RemoveRange(existing);
        var list = entries.ToList();
        if (list.Count > 0) await _context.ArtistTags.AddRangeAsync(list);
        await _context.SaveChangesAsync();
    }

    public async Task<Dictionary<string, Domain.Entities.Media.Artist>> GetArtistsByNamesAsync(IEnumerable<string> names, MusicAccessFilter access)
    {
        var nameList = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.ToLower()).Distinct().ToList();
        if (nameList.Count == 0) return new Dictionary<string, Domain.Entities.Media.Artist>(StringComparer.OrdinalIgnoreCase);

        var query = _context.Artists.AsNoTracking().Where(a => nameList.Contains(a.Name.ToLower()));
        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(a => allowed.Contains(a.LibraryId));
        }

        var rows = await query.ToListAsync();
        return rows.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<Guid>> GetActiveArtistIdsForProfileAsync(Guid profileId, int withinDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-withinDays);

        var artistIds = await _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.PlayedAt >= cutoff)
            .Join(_context.Tracks, p => p.TrackId, t => t.Id, (p, t) => t)
            .Where(t => t.AlbumId != null)
            .Join(_context.Albums, t => t.AlbumId, a => (Guid?)a.Id, (t, a) => a.ArtistId)
            .Distinct()
            .ToListAsync();

        return artistIds;
    }

    public async Task<List<Track>> GetRecentlyAddedTracksByArtistsAsync(IEnumerable<Guid> artistIds, MusicAccessFilter access, int withinDays, int limit)
    {
        var artistList = artistIds.Distinct().ToList();
        if (artistList.Count == 0) return new List<Track>();

        var cutoff = DateTime.UtcNow.AddDays(-withinDays);

        var query = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)!
                .ThenInclude(a => a!.Artist)
            .Where(t => t.AddedAt >= cutoff && t.AlbumId != null && t.Album != null && artistList.Contains(t.Album.ArtistId));

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(t => t.Album != null && allowed.Contains(t.Album.LibraryId));
        }

        if (!access.HasAllRatings)
        {
            var allowedRatings = access.AllowedRatings;
            if (access.BlockUnratedContent)
            {
                query = query.Where(t => t.ContentRating != null && allowedRatings.Contains(t.ContentRating));
            }
            else
            {
                query = query.Where(t => t.ContentRating == null || allowedRatings.Contains(t.ContentRating));
            }
        }

        return await query
            .OrderByDescending(t => t.AddedAt)
            .ThenBy(t => t.TrackNumber)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<HashSet<Guid>> GetArtistsFirstPlayedInYearAsync(Guid profileId, MusicAccessFilter access, int year)
    {
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = yearStart.AddYears(1);

        var query = _context.TrackPlayHistory
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId)
            .Join(_context.Tracks, p => p.TrackId, t => t.Id, (p, t) => new { Play = p, Track = t })
            .Where(x => x.Track.AlbumId != null)
            .Join(_context.Albums, x => x.Track.AlbumId, a => (Guid?)a.Id, (x, a) => new { x.Play, x.Track, Album = a });

        if (!access.HasAllLibraryAccess)
        {
            var allowed = access.AllowedLibraryIds;
            query = query.Where(x => allowed.Contains(x.Album.LibraryId));
        }

        var firstPlays = await query
            .GroupBy(x => x.Album.ArtistId)
            .Select(g => new { ArtistId = g.Key, FirstAt = g.Min(p => p.Play.PlayedAt) })
            .Where(g => g.FirstAt >= yearStart && g.FirstAt < yearEnd)
            .Select(g => g.ArtistId)
            .ToListAsync();

        return firstPlays.ToHashSet();
    }
}
