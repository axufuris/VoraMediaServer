using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Application.Media;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups;

public sealed class BackupReferenceMapper
{
    private const string TrackKeyPrefix = "track:";

    private readonly VoraDbContext _db;
    private readonly Dictionary<string, List<BackupItemIdentity>> _liveIdentityCache = new(StringComparer.Ordinal);
    private Dictionary<Guid, string>? _libraryNames;

    public BackupReferenceMapper(VoraDbContext db)
    {
        _db = db;
    }

    public async Task WriteIdentitiesAsync(IBackupWriter writer, string sectionKey, BackupItemReferences references, CancellationToken ct)
    {
        var file = new BackupIdentityFile();
        foreach (var kind in references.Kinds)
        {
            var ids = references.Get(kind).ToList();
            file.For(kind).AddRange(await DescribeAsync(kind, ids, includeVideo: true, includeTracks: true, ct));
        }
        await writer.WriteJsonAsync(BackupIdentityFile.PathFor(sectionKey), file, ct);
    }

    public async Task<BackupReferenceResolver> ReadResolverAsync(IBackupReader reader, string sectionKey, BackupItemReferences references, CancellationToken ct)
    {
        var recorded = await reader.ReadJsonAsync<BackupIdentityFile>(BackupIdentityFile.PathFor(sectionKey), ct);
        var present = new Dictionary<BackupItemKind, HashSet<Guid>>();
        var current = new BackupIdentityFile();

        foreach (var kind in references.Kinds)
        {
            var ids = references.Get(kind);
            var existing = await ExistingAsync(kind, ids.ToList(), ct);
            present[kind] = existing;
            if (recorded == null) continue;

            var unresolved = recorded.For(kind)
                .Where(identity => ids.Contains(identity.Id) && !existing.Contains(identity.Id) && identity.Keys.Count > 0)
                .ToList();
            if (unresolved.Count == 0) continue;

            var keys = unresolved.SelectMany(identity => identity.Keys).ToList();
            var needTracks = keys.Any(k => k.StartsWith(TrackKeyPrefix, StringComparison.Ordinal));
            var needVideo = keys.Any(k => !k.StartsWith(TrackKeyPrefix, StringComparison.Ordinal));
            current.For(kind).AddRange(await DescribeLiveAsync(kind, needVideo, needTracks, ct));
        }

        return new BackupReferenceResolver(present, recorded, current);
    }

    private async Task<HashSet<Guid>> ExistingAsync(BackupItemKind kind, List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new HashSet<Guid>();

        return kind switch
        {
            BackupItemKind.MediaItem => await _db.MediaItems.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToHashSetAsync(ct),
            BackupItemKind.Album => await _db.Albums.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToHashSetAsync(ct),
            BackupItemKind.Artist => await _db.Artists.Where(a => ids.Contains(a.Id)).Select(a => a.Id).ToHashSetAsync(ct),
            BackupItemKind.Library => await _db.MediaLibraries.Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToHashSetAsync(ct),
            BackupItemKind.Collection => await _db.Collections.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToHashSetAsync(ct),
            BackupItemKind.Channel => await _db.IptvChannels.Where(c => ids.Contains(c.Id)).Select(c => c.Id).ToHashSetAsync(ct),
            _ => new HashSet<Guid>()
        };
    }

    private async Task<List<BackupItemIdentity>> DescribeLiveAsync(BackupItemKind kind, bool includeVideo, bool includeTracks, CancellationToken ct)
    {
        var cacheable = kind is BackupItemKind.MediaItem or BackupItemKind.Album or BackupItemKind.Artist;
        if (!cacheable) return await DescribeAsync(kind, null, includeVideo, includeTracks, ct);

        var result = new List<BackupItemIdentity>();
        if (kind != BackupItemKind.MediaItem)
        {
            result.AddRange(await CachedAsync(kind.ToString(), () => DescribeAsync(kind, null, includeVideo, includeTracks, ct)));
            return result;
        }

        if (includeVideo) result.AddRange(await CachedAsync("video", () => DescribeMediaAsync(null, includeVideo: true, includeTracks: false, ct)));
        if (includeTracks) result.AddRange(await CachedAsync("track", () => DescribeMediaAsync(null, includeVideo: false, includeTracks: true, ct)));
        return result;
    }

    private async Task<List<BackupItemIdentity>> CachedAsync(string cacheKey, Func<Task<List<BackupItemIdentity>>> load)
    {
        if (_liveIdentityCache.TryGetValue(cacheKey, out var cached)) return cached;
        var loaded = await load();
        _liveIdentityCache[cacheKey] = loaded;
        return loaded;
    }

    private async Task<List<BackupItemIdentity>> DescribeAsync(BackupItemKind kind, List<Guid>? ids, bool includeVideo, bool includeTracks, CancellationToken ct)
    {
        if (ids is { Count: 0 }) return new List<BackupItemIdentity>();

        switch (kind)
        {
            case BackupItemKind.MediaItem:
                return await DescribeMediaAsync(ids, includeVideo, includeTracks, ct);

            case BackupItemKind.Album:
            {
                var query = _db.Albums.AsNoTracking();
                if (ids != null) query = query.Where(a => ids.Contains(a.Id));
                var sources = await query
                    .Select(a => new MusicAlbumIdentitySource
                    {
                        Id = a.Id,
                        LibraryId = a.LibraryId,
                        Title = a.Title,
                        MusicBrainzId = a.MusicBrainzId,
                        ArtistName = a.Artist.Name
                    })
                    .ToListAsync(ct);
                var names = await LibraryNamesAsync(ct);
                return sources.Select(s => Identity(s.Id, BackupIdentityKeys.ForAlbum(s), names, s.LibraryId)).ToList();
            }

            case BackupItemKind.Artist:
            {
                var query = _db.Artists.AsNoTracking();
                if (ids != null) query = query.Where(a => ids.Contains(a.Id));
                var sources = await query
                    .Select(a => new MusicArtistIdentitySource
                    {
                        Id = a.Id,
                        LibraryId = a.LibraryId,
                        Name = a.Name,
                        MusicBrainzId = a.MusicBrainzId
                    })
                    .ToListAsync(ct);
                var names = await LibraryNamesAsync(ct);
                return sources.Select(s => Identity(s.Id, BackupIdentityKeys.ForArtist(s), names, s.LibraryId)).ToList();
            }

            case BackupItemKind.Library:
            {
                var query = _db.MediaLibraries.AsNoTracking();
                if (ids != null) query = query.Where(l => ids.Contains(l.Id));
                var libraries = await query.Select(l => new { l.Id, l.Name, l.Type }).ToListAsync(ct);
                return libraries
                    .Select(l => new BackupItemIdentity { Id = l.Id, Keys = BackupIdentityKeys.ForLibrary(l.Type.ToString(), l.Name) })
                    .ToList();
            }

            case BackupItemKind.Collection:
            {
                var query = _db.Collections.AsNoTracking();
                if (ids != null) query = query.Where(c => ids.Contains(c.Id));
                var collections = await query
                    .Select(c => new { c.Id, c.TmdbId, c.ImdbId, c.TvdbId, c.Title, c.LibraryId })
                    .ToListAsync(ct);
                var names = await LibraryNamesAsync(ct);
                return collections
                    .Select(c => new BackupItemIdentity
                    {
                        Id = c.Id,
                        Keys = BackupIdentityKeys.ForCollection(c.TmdbId, c.ImdbId, c.TvdbId, c.Title),
                        Library = c.LibraryId.HasValue && names.TryGetValue(c.LibraryId.Value, out var library) ? library : null
                    })
                    .ToList();
            }

            case BackupItemKind.Channel:
            {
                var query = _db.IptvChannels.AsNoTracking();
                if (ids != null) query = query.Where(c => ids.Contains(c.Id));
                var channels = await query.Select(c => new { c.Id, c.PlaylistId, c.ExternalChannelId }).ToListAsync(ct);
                return channels
                    .Select(c => new BackupItemIdentity { Id = c.Id, Keys = BackupIdentityKeys.ForChannel(c.PlaylistId, c.ExternalChannelId) })
                    .ToList();
            }

            default:
                return new List<BackupItemIdentity>();
        }
    }

    private async Task<List<BackupItemIdentity>> DescribeMediaAsync(List<Guid>? ids, bool includeVideo, bool includeTracks, CancellationToken ct)
    {
        var names = await LibraryNamesAsync(ct);
        var result = new List<BackupItemIdentity>();

        if (includeVideo)
        {
            var videos = _db.MediaItems.AsNoTracking().Where(m => !(m is Track));
            videos = ids != null ? videos.Where(m => ids.Contains(m.Id)) : videos.Where(m => m.MissingSince == null);
            var sources = await videos.SelectContentIdentitySource().ToListAsync(ct);
            result.AddRange(sources.Select(s => Identity(s.Id, BackupIdentityKeys.ForVideo(s), names, s.LibraryId)));
        }

        if (includeTracks)
        {
            var tracks = _db.Tracks.AsNoTracking();
            tracks = ids != null ? tracks.Where(t => ids.Contains(t.Id)) : tracks.Where(t => t.MissingSince == null);
            var sources = await tracks
                .Select(t => new MusicTrackIdentitySource
                {
                    Id = t.Id,
                    LibraryId = t.LibraryId,
                    Title = t.Title,
                    TrackNumber = t.TrackNumber,
                    DiscNumber = t.DiscNumber,
                    AlbumTitle = t.Album != null ? t.Album.Title : null,
                    AlbumMusicBrainzId = t.Album != null ? t.Album.MusicBrainzId : null,
                    ArtistName = t.Album != null ? t.Album.Artist.Name : t.Artist
                })
                .ToListAsync(ct);
            result.AddRange(sources.Select(s => Identity(s.Id, BackupIdentityKeys.ForTrack(s), names, s.LibraryId)));
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> LibraryNamesAsync(CancellationToken ct)
    {
        _libraryNames ??= await _db.MediaLibraries.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return _libraryNames;
    }

    private static BackupItemIdentity Identity(Guid id, List<string> keys, Dictionary<Guid, string> libraryNames, Guid libraryId) => new()
    {
        Id = id,
        Keys = keys,
        Library = libraryNames.TryGetValue(libraryId, out var library) ? library : null
    };
}
