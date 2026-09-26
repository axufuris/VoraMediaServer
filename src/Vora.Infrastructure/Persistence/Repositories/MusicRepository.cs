using Microsoft.EntityFrameworkCore;
using Vora.Application.Media;
using Vora.Application.Search.ViewModels;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Persistence.Repositories;

public class MusicRepository : IMusicRepository
{
    // Postgres defaults LIKE's escape character to backslash, but passing it
    // explicitly means the escaping in EscapeLikePattern cannot be silently
    // undone by a server configured otherwise.
    private const string LikeEscapeCharacter = "\\";

    // Clamped rather than trusted, and named rather than inlined: GetAlbumsAsync
    // silently clamping to MaxAlbumPageSize has already cost a debugging session,
    // where a caller asked for the whole library and got 200 rows with nothing
    // saying so. The clamp is right; the silence is what hurt, so the cap is
    // documented on the endpoint.
    private const int MaxTopTracks = 50;

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

    // Pre-filter so a non-forced refresh does not pay a provider round trip for
    // an artist whose slots are all filled. The per-slot lock and force rules
    // still live in MusicManager; this only avoids loading the hopeless cases.
    // Missing something AND not asked recently. Missing alone matched nearly
    // every artist on every scan, since most have no banner or logo anywhere.
    public async Task<List<Guid>> GetArtistIdsForArtworkRefreshAsync(Guid libraryId, bool force, DateTime checkedBefore)
    {
        var query = _context.Set<Artist>().AsNoTracking().Where(a => a.LibraryId == libraryId);

        if (!force)
        {
            query = query.Where(a => (a.ArtworkUrl == null
                    || a.BackgroundUrl == null
                    || a.BannerUrl == null
                    || a.ClearLogoUrl == null)
                && (a.ArtworkCheckedAt == null || a.ArtworkCheckedAt < checkedBefore));
        }

        return await query.OrderBy(a => a.Name).Select(a => a.Id).ToListAsync();
    }

    // As for artists. No provider has album backgrounds, so "missing" alone
    // matched every album on every scan.
    public async Task<List<Guid>> GetAlbumIdsForArtworkRefreshAsync(Guid libraryId, bool force, DateTime checkedBefore)
    {
        var query = _context.Set<Album>().AsNoTracking().Where(a => a.LibraryId == libraryId);

        if (!force)
        {
            query = query.Where(a => (a.ArtworkUrl == null
                    || a.BackgroundUrl == null
                    || a.DiscArtUrl == null)
                && (a.ArtworkCheckedAt == null || a.ArtworkCheckedAt < checkedBefore));
        }

        return await query.OrderBy(a => a.Title).Select(a => a.Id).ToListAsync();
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
        allowedTracks = allowedTracks.ApplyMusicRatings(access);
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

        var allowedTracks = _context.Tracks.AsNoTracking().ApplyMusicRatings(access)
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
        query = query.ApplyMusicRatings(access);

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
        query = query.ApplyMusicRatings(access);

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
        query = query.ApplyMusicRatings(access);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<string?> GetTrackFilePathAsync(Guid trackId, MusicAccessFilter access)
    {
        var trackQuery = _context.Tracks.AsNoTracking().Where(t => t.Id == trackId);
        trackQuery = ApplyLibraryFilter(trackQuery, access);
        trackQuery = trackQuery.ApplyMusicRatings(access);

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
        query = query.ApplyMusicRatings(access);

        var ordered = from track in query
                      join like in likedJoin on track.Id equals like.TrackId
                      orderby like.LikedAt descending
                      select track;

        return await ordered.ToListAsync();
    }

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
        tracks = tracks.ApplyMusicRatings(access);

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
        tracks = tracks.ApplyMusicRatings(access);

        var query = from track in tracks
                    join stat in perTrackCount on track.Id equals stat.TrackId
                    orderby stat.Plays descending, stat.LastPlayed descending
                    select track;

        return await query.Take(Math.Max(1, limit)).ToListAsync();
    }

    // What this artist actually gets played, for the flat list at the top of an
    // artist page. The clients built that section as the first ten of
    // GetTracksForArtistAsync, which orders by album year — so "Tracks" was
    // tracks 1-10 of the artist's oldest album, sitting directly above an Albums
    // rail whose first card was that same album.
    //
    // LEFT join, unlike GetTopPlayedTracksAsync next door. That one inner-joins
    // because it answers "what have I played most", where a track with no plays
    // has no place. Here an inner join would empty the section on any artist
    // nobody has played yet — every artist on a new install — so unplayed tracks
    // still come back, behind the played ones, in the order the clients show
    // today. The change degrades to the status quo rather than to an empty box.
    //
    // Play counts are server-wide on purpose, with no profile filter. This is a
    // household server and the page already carries "Also Played Here", which is
    // explicitly about everyone. An artist page that looked different to every
    // member of the house would be surprising for no stated benefit. Access
    // filtering is untouched and still per-profile.
    public async Task<List<Track>> GetTopTracksForArtistAsync(Guid artistId, MusicAccessFilter access, int limit)
    {
        var albumIds = await _context.Albums
            .AsNoTracking()
            .Where(a => a.ArtistId == artistId)
            .Select(a => a.Id)
            .ToListAsync();

        if (albumIds.Count == 0) return new List<Track>();

        IQueryable<Track> tracks = _context.Tracks
            .AsNoTracking()
            .Include(t => t.Album)
            .Where(t => t.AlbumId != null && albumIds.Contains(t.AlbumId.Value));
        tracks = ApplyLibraryFilter(tracks, access);
        tracks = tracks.ApplyMusicRatings(access);

        // Counted per track rather than group-joined. A group join plus
        // DefaultIfEmpty needs a null check on every sort key, and those ternaries
        // are what the in-memory provider chokes on while sorting. Count returns 0
        // for a track nobody has played and Max over nothing returns null once the
        // cast makes it nullable, so the left join falls out and no key can be
        // null-unwrapped. It is a correlated subquery per row, over one artist's
        // tracks, capped at fifty.
        var query = tracks
            .Select(t => new
            {
                Track = t,
                Plays = _context.TrackPlayHistory.Count(h => h.TrackId == t.Id),
                LastPlayed = _context.TrackPlayHistory
                    .Where(h => h.TrackId == t.Id)
                    .Max(h => (DateTime?)h.PlayedAt)
            })
            .OrderByDescending(x => x.Plays)
            .ThenByDescending(x => x.LastPlayed)
            // Then the world's opinion. This is what an artist nobody here has
            // played is ordered by: their actual hits rather than the first
            // tracks of their oldest album, which is the slice this section was
            // introduced to replace. HasValue first so tracks with no figure sink
            // rather than Postgres putting NULLs first in a descending sort.
            .ThenByDescending(x => x.Track.GlobalListeners.HasValue)
            .ThenByDescending(x => x.Track.GlobalListeners)
            // And only then album order, for tracks nobody anywhere has an
            // opinion on.
            .ThenBy(x => x.Track.Album == null ? null : x.Track.Album.Year)
            .ThenBy(x => x.Track.DiscNumber)
            .ThenBy(x => x.Track.TrackNumber)
            .Select(x => x.Track);

        return await query.Take(Math.Clamp(limit, 1, MaxTopTracks)).ToListAsync();
    }
    // Never-refreshed artists first, then the stalest. Ordered so that a batch
    // cut short by its limit or by the provider going away still spends itself on
    // the artists with the least data, not on ones refreshed last month.
    public Task<List<PopularityRefreshTarget>> GetArtistsDueForPopularityRefreshAsync(DateTime staleBefore, int limit) =>
        _context.Artists
            .AsNoTracking()
            .Where(a => a.PopularityRefreshedAt == null || a.PopularityRefreshedAt < staleBefore)
            .OrderBy(a => a.PopularityRefreshedAt.HasValue)
            .ThenBy(a => a.PopularityRefreshedAt)
            .ThenBy(a => a.Name)
            .Take(Math.Max(1, limit))
            .Select(a => new PopularityRefreshTarget(a.Id, a.Name))
            .ToListAsync();

    // Tracked, with every album and every track, because a refresh rewrites the
    // artist's whole catalogue as one snapshot.
    public Task<Artist?> GetArtistCatalogForUpdateAsync(Guid artistId) =>
        _context.Artists
            .Include(a => a.Albums)
            .ThenInclude(al => al.Tracks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == artistId);

    // Albums holding a track with no rating that a provider has not been asked
    // about, or was asked long enough ago to be worth asking again. Never-asked
    // first, so a run cut short still spends itself on the unrated.
    public Task<List<ContentRatingTarget>> GetAlbumsDueForContentRatingAsync(DateTime recheckBefore, int limit)
    {
        var awaiting = _context.Tracks.AwaitingProviderRating(recheckBefore);
        return _context.Albums
            .AsNoTracking()
            .Where(al => awaiting.Any(t => t.AlbumId == al.Id))
            .OrderBy(al => awaiting.Any(t => t.AlbumId == al.Id && t.ContentRatingCheckedAt != null))
            .ThenBy(al => al.Title)
            .Take(Math.Max(1, limit))
            .Select(al => new ContentRatingTarget(al.Id, al.AlbumArtist ?? al.Artist.Name, al.Title))
            .ToListAsync();
    }

    public Task<List<TrackForEmbedding>> GetTracksMissingEmbeddingsAsync(int limit) =>
        _context.Tracks
            .AsNoTracking()
            .Where(t => !_context.MediaItemEmbeddings.Any(e => e.MediaItemId == t.Id))
            .OrderBy(t => t.Id)
            .Take(Math.Max(1, limit))
            .Select(t => new TrackForEmbedding(
                t.Id,
                t.Title,
                t.Artist ?? (t.Album != null ? t.Album.Artist.Name : null),
                t.Album != null ? t.Album.Title : null,
                t.Album != null ? t.Album.Year : null,
                t.Album != null ? t.Album.Genre : null))
            .ToListAsync();

    public Task<int> CountTracksMissingEmbeddingsAsync() =>
        _context.Tracks.CountAsync(t => !_context.MediaItemEmbeddings.Any(e => e.MediaItemId == t.Id));

    public async Task SaveTrackEmbeddingsAsync(IReadOnlyList<(Guid TrackId, float[] Vector)> embeddings)
    {
        foreach (var (trackId, vector) in embeddings)
        {
            _context.MediaItemEmbeddings.Add(new Vora.Domain.Entities.Ai.MediaItemEmbedding
            {
                MediaItemId = trackId,
                Embedding = new Pgvector.Vector(vector),
                LastUpdatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
    }

    public Task<List<Track>> GetAlbumTracksForUpdateAsync(Guid albumId) =>
        _context.Tracks
            .Where(t => t.AlbumId == albumId)
            .OrderBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .ToListAsync();

    public Task SaveMusicChangesAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task<List<Album>> GetRecentlyAddedAlbumsAsync(MusicAccessFilter access, int limit)
    {
        IQueryable<Album> query = _context.Albums.AsNoTracking().Include(a => a.Artist);
        query = ApplyLibraryFilter(query, access);

        var allowedTracks = _context.Tracks.AsNoTracking().ApplyMusicRatings(access)
            .Where(t => t.AlbumId != null)
            .Select(t => t.AlbumId!.Value);

        query = query.Where(a => allowedTracks.Contains(a.Id));

        return await query
            .OrderByDescending(a => a.AddedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync();
    }

    // A user typing % or _ means those characters, not "match anything" and
    // "match one of anything". Backslash goes first, or it would escape the
    // escapes added after it.
    internal static string EscapeLikePattern(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<(List<Album> Albums, int Total)> GetAlbumsPageAsync(Guid? libraryId, MusicAccessFilter access, AlbumSortOrder sort, int offset, int limit, string? search = null)
    {
        IQueryable<Album> query = _context.Albums.AsNoTracking();
        if (libraryId.HasValue)
        {
            var id = libraryId.Value;
            query = query.Where(a => a.LibraryId == id);
        }
        query = ApplyLibraryFilter(query, access);

        var playableAlbumIds = _context.Tracks.AsNoTracking().ApplyMusicRatings(access)
            .Where(t => t.AlbumId != null)
            .Select(t => t.AlbumId);

        query = query.Where(a => playableAlbumIds.Contains(a.Id));

        // After the access filters and before the count, so a search can only ever
        // narrow what the profile could already see, and Total counts matches
        // rather than the library — otherwise the client pages through a total it
        // will never reach.
        //
        // Title OR artist name, because the card's primary label is the artist and
        // its secondary is the album, so a user typing either expects a hit.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLikePattern(search.Trim())}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Title, pattern, LikeEscapeCharacter)
                || EF.Functions.ILike(a.Artist.Name, pattern, LikeEscapeCharacter));
        }

        var total = await query.CountAsync();

        // A-Z follows the ARTIST, because that is what the album card shows as its
        // title — the album name sits underneath in smaller text next to the year.
        // Ordering by album title made an A-Z grid look unsorted: the big labels
        // ran in no discernible order.
        var ordered = sort switch
        {
            AlbumSortOrder.Alphabetical => query
                .OrderBy(a => a.Artist!.SortName ?? a.Artist!.Name)
                .ThenBy(a => a.SortTitle ?? a.Title)
                .ThenBy(a => a.Id),

            // HasValue first so albums with no figure sink to the end instead of
            // Postgres putting NULLs first in a descending sort.
            AlbumSortOrder.Popular => query
                .OrderByDescending(a => a.GlobalPlays.HasValue)
                .ThenByDescending(a => a.GlobalPlays)
                .ThenBy(a => a.Artist!.SortName ?? a.Artist!.Name)
                .ThenBy(a => a.Id),

            _ => query.OrderByDescending(a => a.AddedAt).ThenBy(a => a.Id),
        };

        var albums = await ordered
            .Skip(offset)
            .Take(limit)
            .Include(a => a.Artist)
            .ToListAsync();

        return (albums, total);
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

    // Name-only lookups for the MusicBrainz id cache. A provider is handed the
    // artist and album as text — it never sees an id — so this is the only handle
    // it has, and it is the same handle the scanner groups by.
    public Task<Artist?> FindArtistByNameAsync(string name) =>
        _context.Artists.FirstOrDefaultAsync(a => a.Name == name);

    public Task<Album?> FindAlbumByArtistAndTitleAsync(string artistName, string albumTitle) =>
        _context.Albums
            .Where(a => a.Title == albumTitle && a.Artist.Name == artistName)
            .FirstOrDefaultAsync();
    // What else the people who play this artist play, across every profile on the
    // server. Ranked by how MANY of them overlap first and total plays second, so
    // one profile looping an album cannot outrank an artist several people share.
    //
    // Deliberately server-wide rather than per-profile: the Music page's For You
    // tab is the personal view, and this row exists to answer the different
    // question of what this server listens to alongside the artist.
    public async Task<List<Artist>> GetCoPlayedArtistsAsync(Guid artistId, MusicAccessFilter access, int limit)
    {
        var listeners = from history in _context.TrackPlayHistory.AsNoTracking()
                        join track in _context.Tracks.AsNoTracking() on history.TrackId equals track.Id
                        join album in _context.Albums.AsNoTracking() on track.AlbumId equals album.Id
                        where album.ArtistId == artistId
                        select history.ProfileId;

        var coPlays = from history in _context.TrackPlayHistory.AsNoTracking()
                      where listeners.Contains(history.ProfileId)
                      join track in _context.Tracks.AsNoTracking() on history.TrackId equals track.Id
                      join album in _context.Albums.AsNoTracking() on track.AlbumId equals album.Id
                      where album.ArtistId != artistId
                      group history by album.ArtistId into grp
                      select new
                      {
                          ArtistId = grp.Key,
                          Listeners = grp.Select(h => h.ProfileId).Distinct().Count(),
                          Plays = grp.Count()
                      };

        IQueryable<Artist> artists = _context.Artists.AsNoTracking();
        artists = ApplyLibraryFilter(artists, access);

        var query = from artist in artists
                    join stat in coPlays on artist.Id equals stat.ArtistId
                    orderby stat.Listeners descending, stat.Plays descending, artist.Name
                    select artist;

        return await query.Take(Math.Max(1, limit)).ToListAsync();
    }

    public async Task<List<MusicSearchResultVM>> SearchAsync(string query, MusicAccessFilter access, int limit)
    {
        var searchPattern = $"%{query}%";
        var perTypeLimit = Math.Max(1, limit);

        var artistsQuery = ApplyLibraryFilter(_context.Artists.AsNoTracking(), access)
            .Where(a => EF.Functions.ILike(a.Name, searchPattern));

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
            .Where(a => EF.Functions.ILike(a.Title, searchPattern));

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
        tracksQuery = tracksQuery.ApplyMusicRatings(access);
        tracksQuery = tracksQuery.Where(t => EF.Functions.ILike(t.Title, searchPattern));

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
        trackQuery = trackQuery.ApplyMusicRatings(access);

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
            var pattern = $"%{search}%";
            joined = joined.Where(j =>
                EF.Functions.ILike(j.x.Track.Title, pattern)
                || (j.Album != null && EF.Functions.ILike(j.Album.Title, pattern))
                || (j.Artist != null && EF.Functions.ILike(j.Artist.Name, pattern))
                || (j.x.Track.Artist != null && EF.Functions.ILike(j.x.Track.Artist, pattern))
                || EF.Functions.ILike(j.ProfileName, pattern));
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
        var albumQuery = _context.Albums.AsNoTracking().Include(a => a.Artist).Where(a => a.Genre != null && EF.Functions.ILike(a.Genre, genre));
        albumQuery = ApplyLibraryFilter(albumQuery, access);
        var albums = await albumQuery.OrderByDescending(a => a.AddedAt).Take(60).ToListAsync();

        var artistIds = albums.Select(a => a.ArtistId).Distinct().ToList();
        var artists = await _context.Artists.AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .Take(40)
            .ToListAsync();

        var trackQuery = _context.Tracks.AsNoTracking().Include(t => t.Album)
            .Where(t => t.Album != null && t.Album.Genre != null && EF.Functions.ILike(t.Album.Genre, genre));
        trackQuery = ApplyLibraryFilter(trackQuery, access);
        trackQuery = trackQuery.ApplyMusicRatings(access);
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
