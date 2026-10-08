using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Application.Metadata;
using Vora.Domain.Entities.Common;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class LibraryDefinitionsBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;

    public LibraryDefinitionsBackupSection(VoraDbContext db)
    {
        _db = db;
    }

    public string Key => BackupManager.LibraryDefinitionsKey;
    public string DisplayName => "Libraries";
    public BackupSectionGroup Group => BackupSectionGroup.Library;
    public bool RequiresExplicitConfirm => false;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Adds the libraries in the backup that aren't on this server and updates the folders and settings of libraries with the same name and type. Other libraries are left alone. Scan the libraries afterwards to bring their titles in.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct) =>
        await writer.WriteJsonAsync($"{Key}/libraries.json", await _db.MediaLibraries.AsNoTracking().ToListAsync(ct), ct);

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var libraries = await reader.ReadJsonAsync<List<MediaLibrary>>($"{Key}/libraries.json", ct);
        if (libraries == null) return new BackupSectionImportResult();

        var existing = await _db.MediaLibraries.ToListAsync(ct);
        var added = new List<string>();
        foreach (var library in libraries)
        {
            library.MediaItems = new List<MediaItem>();
            var match = existing.FirstOrDefault(l => l.Type == library.Type && string.Equals(l.Name, library.Name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                library.Id = match.Id;
                _db.Entry(match).CurrentValues.SetValues(library);
                continue;
            }

            if (existing.Any(l => l.Id == library.Id)) library.Id = Guid.NewGuid();
            _db.MediaLibraries.Add(library);
            existing.Add(library);
            added.Add(library.Name);
        }

        await _db.SaveChangesAsync(ct);

        var result = new BackupSectionImportResult { RowsImported = libraries.Count };
        if (added.Count == 1)
        {
            result.Warnings.Add($"Added the library {added[0]}. Scan it to bring its titles in, then restore the user-data sections again.");
        }
        else if (added.Count > 1)
        {
            result.Warnings.Add($"Added {added.Count} libraries ({string.Join(", ", added)}). Scan them to bring their titles in, then restore the user-data sections again.");
        }
        return result;
    }
}

public enum MediaEditFileLeadsTo
{
    Item,
    SeasonOfEpisode,
    ShowOfEpisode,
    AlbumOfTrack,
    ArtistOfTrack
}

public sealed class MediaEditRecord
{
    public BackupItemKind Kind { get; set; }
    public Guid Id { get; set; }
    public string? FilePath { get; set; }
    public MediaEditFileLeadsTo FileLeadsTo { get; set; }
    public List<string> LockedFields { get; set; } = new();
    public Dictionary<string, JsonElement> Values { get; set; } = new();
    public List<MediaItemMarker>? Markers { get; set; }
    public List<MediaArtwork>? UploadedArtwork { get; set; }
    public List<MediaEditSubtitle>? Subtitles { get; set; }
}

public sealed class MediaEditSubtitle
{
    public string PartFilePath { get; set; } = string.Empty;
    public MediaSubtitleTrack Track { get; set; } = new();
}

public sealed class MediaEditsBackupSection : IBackupSection
{
    public const string MarkersLock = "Markers";
    private static readonly BackupRowNoun Noun = new("edited title", "edited titles");
    private static readonly BackupRowNoun SubtitleNoun = new("downloaded subtitle", "downloaded subtitles");
    private static readonly string[] NeverRestored = { "Id", "LibraryId", "LockedFields" };
    private static readonly string[] MatchIds = { nameof(MediaItem.TmdbId), nameof(MediaItem.ImdbId), nameof(MediaItem.TvdbId) };

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public MediaEditsBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "library.media-edits";
    public string DisplayName => "Metadata Edits & Locks";
    public BackupSectionGroup Group => BackupSectionGroup.Library;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Puts back hand-made changes to titles, albums and artists: locked metadata and artwork choices, hand-edited markers, fixed matches, uploaded artwork and downloaded subtitles. Titles are found by their file first. Scan the libraries before restoring this.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var noLocks = new List<string>();
        var items = (await _db.MediaItems.AsNoTracking().Where(m => m.LockedFields != noLocks).ToListAsync(ct))
            .Where(m => m.LockedFields.Count > 0)
            .ToDictionary(m => m.Id);
        var albums = (await _db.Albums.AsNoTracking().Where(a => a.LockedFields != noLocks).ToListAsync(ct)).Where(a => a.LockedFields.Count > 0).ToList();
        var artists = (await _db.Artists.AsNoTracking().Where(a => a.LockedFields != noLocks).ToListAsync(ct)).Where(a => a.LockedFields.Count > 0).ToList();

        var uploads = (await _db.MediaArtwork.AsNoTracking().Where(a => a.IsUserUploaded).ToListAsync(ct)).ToLookup(a => a.MediaItemId);
        var downloads = (await (
                from track in _db.MediaSubtitleTracks.AsNoTracking()
                where track.IsDownloaded
                join part in _db.MediaParts.AsNoTracking() on track.MediaPartId equals part.Id
                select new { Track = track, part.FilePath, ItemId = part.MediaItemId ?? Guid.Empty })
            .ToListAsync(ct))
            .ToLookup(d => d.ItemId);

        var extraIds = uploads.Select(g => g.Key).Concat(downloads.Select(g => g.Key)).Where(id => !items.ContainsKey(id)).Distinct().ToList();
        foreach (var item in await _db.MediaItems.AsNoTracking().Where(m => extraIds.Contains(m.Id)).ToListAsync(ct)) items[item.Id] = item;

        var markerIds = items.Values.Where(m => m.IsLocked(MarkersLock)).Select(m => m.Id).ToList();
        var markers = (await _db.MediaItemMarkers.AsNoTracking().Where(k => markerIds.Contains(k.MediaItemId)).ToListAsync(ct)).ToLookup(k => k.MediaItemId);

        var files = await FilesAsync(items.Values.ToList(), albums.Select(a => a.Id).ToList(), artists.Select(a => a.Id).ToList(), ct);

        var records = new List<MediaEditRecord>();
        foreach (var item in items.Values)
        {
            var (file, leadsTo) = files.GetValueOrDefault(item.Id);
            records.Add(new MediaEditRecord
            {
                Kind = BackupItemKind.MediaItem,
                Id = item.Id,
                FilePath = file,
                FileLeadsTo = leadsTo,
                LockedFields = item.LockedFields,
                Values = LockedValues(item),
                Markers = item.IsLocked(MarkersLock) ? markers[item.Id].ToList() : null,
                UploadedArtwork = uploads.Contains(item.Id) ? uploads[item.Id].ToList() : null,
                Subtitles = downloads.Contains(item.Id)
                    ? downloads[item.Id].Select(d => new MediaEditSubtitle { PartFilePath = d.FilePath, Track = d.Track }).ToList()
                    : null
            });
        }
        foreach (var album in albums)
        {
            var (file, leadsTo) = files.GetValueOrDefault(album.Id);
            records.Add(new MediaEditRecord { Kind = BackupItemKind.Album, Id = album.Id, FilePath = file, FileLeadsTo = leadsTo, LockedFields = album.LockedFields, Values = LockedValues(album) });
        }
        foreach (var artist in artists)
        {
            var (file, leadsTo) = files.GetValueOrDefault(artist.Id);
            records.Add(new MediaEditRecord { Kind = BackupItemKind.Artist, Id = artist.Id, FilePath = file, FileLeadsTo = leadsTo, LockedFields = artist.LockedFields, Values = LockedValues(artist) });
        }

        await writer.WriteJsonAsync($"{Key}/edits.json", records, ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(records), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var records = await reader.ReadJsonAsync<List<MediaEditRecord>>($"{Key}/edits.json", ct);
        if (records == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var resolver = await _references.ReadResolverAsync(reader, Key, References(records), ct);
        var locator = await TargetLocator.LoadAsync(_db, records, ct);

        var located = new List<(MediaEditRecord Record, Guid Target)>();
        foreach (var record in records)
        {
            var target = locator.Find(record) ?? resolver.Resolve(record.Kind, record.Id);
            if (target == null)
            {
                tally.Skip(Noun, BackupSkipReason.MissingItem);
                continue;
            }
            located.Add((record, target.Value));
        }

        var itemIds = located.Where(l => l.Record.Kind == BackupItemKind.MediaItem).Select(l => l.Target).ToList();
        var albumIds = located.Where(l => l.Record.Kind == BackupItemKind.Album).Select(l => l.Target).ToList();
        var artistIds = located.Where(l => l.Record.Kind == BackupItemKind.Artist).Select(l => l.Target).ToList();
        var items = await _db.MediaItems.Where(m => itemIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var albums = await _db.Albums.Where(a => albumIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var artists = await _db.Artists.Where(a => artistIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        var rematched = 0;
        foreach (var (record, target) in located)
        {
            LockableEntity? entity = record.Kind switch
            {
                BackupItemKind.MediaItem => items.GetValueOrDefault(target),
                BackupItemKind.Album => albums.GetValueOrDefault(target),
                BackupItemKind.Artist => artists.GetValueOrDefault(target),
                _ => null
            };
            if (entity == null) continue;

            ApplyValues(entity, record.Values);
            entity.LockedFields = entity.LockedFields.Union(record.LockedFields, StringComparer.OrdinalIgnoreCase).ToList();
            if (record.LockedFields.Contains(MediaMatchManager.MatchLock, StringComparer.OrdinalIgnoreCase)) rematched++;

            if (record.Markers != null) await ReplaceMarkersAsync(target, record.Markers, ct);
            if (record.UploadedArtwork != null) await AddUploadedArtworkAsync(target, record.UploadedArtwork, ct);
            if (record.Subtitles != null) await AddSubtitlesAsync(record.Subtitles, locator, tally, ct);
        }

        await _db.SaveChangesAsync(ct);

        var result = tally.ToResult(located.Count);
        if (rematched > 0)
        {
            result.Warnings.Add($"{rematched} {(rematched == 1 ? "title was" : "titles were")} matched again by hand. Refresh {(rematched == 1 ? "its" : "their")} library's metadata to bring in the matching details and artwork.");
        }
        return result;
    }

    private async Task ReplaceMarkersAsync(Guid itemId, List<MediaItemMarker> markers, CancellationToken ct)
    {
        _db.MediaItemMarkers.RemoveRange(await _db.MediaItemMarkers.Where(k => k.MediaItemId == itemId).ToListAsync(ct));
        _db.MediaItemMarkers.AddRange(markers.Select(k => new MediaItemMarker
        {
            MediaItemId = itemId,
            Type = k.Type,
            Start = k.Start,
            End = k.End,
            Order = k.Order
        }));
    }

    private async Task AddUploadedArtworkAsync(Guid itemId, List<MediaArtwork> artwork, CancellationToken ct)
    {
        var urls = await _db.MediaArtwork.Where(a => a.MediaItemId == itemId).Select(a => a.Url).ToListAsync(ct);
        foreach (var image in artwork.Where(a => !urls.Contains(a.Url)))
        {
            _db.MediaArtwork.Add(new MediaArtwork
            {
                MediaItemId = itemId,
                Url = image.Url,
                Kind = image.Kind,
                Language = image.Language,
                Width = image.Width,
                Height = image.Height,
                VoteAverage = image.VoteAverage,
                ProviderId = image.ProviderId,
                IsUserUploaded = true
            });
        }
    }

    private async Task AddSubtitlesAsync(List<MediaEditSubtitle> subtitles, TargetLocator locator, BackupSkipTally tally, CancellationToken ct)
    {
        foreach (var subtitle in subtitles)
        {
            var partId = locator.PartId(subtitle.PartFilePath);
            if (partId == null)
            {
                tally.Skip(SubtitleNoun, BackupSkipReason.MissingItem);
                continue;
            }

            var file = subtitle.Track.ExternalFilePath;
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                tally.Skip(SubtitleNoun, BackupSkipReason.MissingFile);
                continue;
            }

            if (await _db.MediaSubtitleTracks.AnyAsync(t => t.MediaPartId == partId.Value && t.ExternalFilePath == file, ct)) continue;

            subtitle.Track.Id = Guid.NewGuid();
            subtitle.Track.MediaPartId = partId.Value;
            _db.MediaSubtitleTracks.Add(subtitle.Track);
        }
    }

    internal static Dictionary<string, JsonElement> LockedValues(LockableEntity entity)
    {
        var names = entity.LockedFields.ToList();
        if (names.Contains(MediaMatchManager.MatchLock, StringComparer.OrdinalIgnoreCase)) names.AddRange(MatchIds);

        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var property = Restorable(entity, name);
            if (property == null) continue;
            values[property.Name] = JsonSerializer.SerializeToElement(property.GetValue(entity), property.PropertyType, BackupJson.Options);
        }
        return values;
    }

    internal static void ApplyValues(LockableEntity entity, Dictionary<string, JsonElement> values)
    {
        foreach (var (name, value) in values)
        {
            var property = Restorable(entity, name);
            if (property == null) continue;
            property.SetValue(entity, value.Deserialize(property.PropertyType, BackupJson.Options));
        }
    }

    private static PropertyInfo? Restorable(LockableEntity entity, string name)
    {
        if (NeverRestored.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
        var property = entity.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property is { CanRead: true, CanWrite: true } && IsPlainValue(property.PropertyType) ? property : null;
    }

    private static bool IsPlainValue(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain.IsPrimitive || plain.IsEnum || plain == typeof(string) || plain == typeof(decimal) || plain == typeof(DateTime)
            || plain == typeof(DateOnly) || plain == typeof(TimeSpan) || plain == typeof(Guid) || plain == typeof(List<string>);
    }

    private async Task<Dictionary<Guid, (string? File, MediaEditFileLeadsTo LeadsTo)>> FilesAsync(
        List<MediaItem> items, List<Guid> albumIds, List<Guid> artistIds, CancellationToken ct)
    {
        var files = new Dictionary<Guid, (string?, MediaEditFileLeadsTo)>();

        var ownIds = items.Select(i => i.Id).ToList();
        foreach (var part in await _db.MediaParts.AsNoTracking()
                     .Where(p => p.MediaItemId.HasValue && ownIds.Contains(p.MediaItemId.Value))
                     .Select(p => new { ItemId = p.MediaItemId ?? Guid.Empty, p.FilePath })
                     .ToListAsync(ct))
        {
            files.TryAdd(part.ItemId, (part.FilePath, MediaEditFileLeadsTo.Item));
        }

        var seasonIds = items.OfType<Season>().Select(s => s.Id).ToList();
        var showIds = items.OfType<TvShow>().Select(s => s.Id).ToList();
        var seasons = await _db.Set<Season>().AsNoTracking()
            .Where(s => seasonIds.Contains(s.Id) || showIds.Contains(s.TvShowId))
            .Select(s => new { s.Id, s.TvShowId })
            .ToListAsync(ct);
        var allSeasonIds = seasons.Select(s => s.Id).ToList();
        var episodeFiles = await (
                from episode in _db.Set<Episode>().AsNoTracking()
                where allSeasonIds.Contains(episode.SeasonId)
                join part in _db.MediaParts.AsNoTracking() on (Guid?)episode.Id equals part.MediaItemId
                select new { episode.SeasonId, part.FilePath })
            .ToListAsync(ct);
        foreach (var episode in episodeFiles)
        {
            if (seasonIds.Contains(episode.SeasonId)) files.TryAdd(episode.SeasonId, (episode.FilePath, MediaEditFileLeadsTo.SeasonOfEpisode));
            var showId = seasons.First(s => s.Id == episode.SeasonId).TvShowId;
            if (showIds.Contains(showId)) files.TryAdd(showId, (episode.FilePath, MediaEditFileLeadsTo.ShowOfEpisode));
        }

        var trackFiles = await (
                from track in _db.Set<Track>().AsNoTracking()
                where track.AlbumId.HasValue
                join album in _db.Albums.AsNoTracking() on track.AlbumId equals album.Id
                where albumIds.Contains(album.Id) || artistIds.Contains(album.ArtistId)
                join part in _db.MediaParts.AsNoTracking() on (Guid?)track.Id equals part.MediaItemId
                select new { AlbumId = album.Id, album.ArtistId, part.FilePath })
            .ToListAsync(ct);
        foreach (var track in trackFiles)
        {
            if (albumIds.Contains(track.AlbumId)) files.TryAdd(track.AlbumId, (track.FilePath, MediaEditFileLeadsTo.AlbumOfTrack));
            if (artistIds.Contains(track.ArtistId)) files.TryAdd(track.ArtistId, (track.FilePath, MediaEditFileLeadsTo.ArtistOfTrack));
        }

        return files;
    }

    private static BackupItemReferences References(List<MediaEditRecord> records) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.MediaItem, records.Where(r => r.Kind == BackupItemKind.MediaItem).Select(r => r.Id))
            .AddRange(BackupItemKind.Album, records.Where(r => r.Kind == BackupItemKind.Album).Select(r => r.Id))
            .AddRange(BackupItemKind.Artist, records.Where(r => r.Kind == BackupItemKind.Artist).Select(r => r.Id));

    private sealed class TargetLocator
    {
        private readonly Dictionary<string, (Guid PartId, Guid ItemId)> _parts;
        private readonly Dictionary<Guid, Guid> _seasonOfEpisode;
        private readonly Dictionary<Guid, Guid> _showOfSeason;
        private readonly Dictionary<Guid, Guid> _albumOfTrack;
        private readonly Dictionary<Guid, Guid> _artistOfAlbum;

        private TargetLocator(
            Dictionary<string, (Guid, Guid)> parts,
            Dictionary<Guid, Guid> seasonOfEpisode,
            Dictionary<Guid, Guid> showOfSeason,
            Dictionary<Guid, Guid> albumOfTrack,
            Dictionary<Guid, Guid> artistOfAlbum)
        {
            _parts = parts;
            _seasonOfEpisode = seasonOfEpisode;
            _showOfSeason = showOfSeason;
            _albumOfTrack = albumOfTrack;
            _artistOfAlbum = artistOfAlbum;
        }

        public static async Task<TargetLocator> LoadAsync(VoraDbContext db, List<MediaEditRecord> records, CancellationToken ct)
        {
            var files = records.Select(r => r.FilePath)
                .Concat(records.SelectMany(r => r.Subtitles ?? new List<MediaEditSubtitle>()).Select(s => s.PartFilePath))
                .OfType<string>()
                .Distinct()
                .ToList();

            var parts = (await db.MediaParts.AsNoTracking()
                    .Where(p => p.MediaItemId.HasValue && files.Contains(p.FilePath))
                    .Select(p => new { p.Id, p.FilePath, ItemId = p.MediaItemId ?? Guid.Empty })
                    .ToListAsync(ct))
                .GroupBy(p => p.FilePath, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().ItemId), StringComparer.Ordinal);

            var itemIds = parts.Values.Select(p => p.Item2).ToList();
            var seasonOfEpisode = await db.Set<Episode>().AsNoTracking().Where(e => itemIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.SeasonId, ct);
            var seasonIds = seasonOfEpisode.Values.Distinct().ToList();
            var showOfSeason = await db.Set<Season>().AsNoTracking().Where(s => seasonIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.TvShowId, ct);
            var albumOfTrack = (await db.Set<Track>().AsNoTracking().Where(t => itemIds.Contains(t.Id) && t.AlbumId.HasValue).Select(t => new { t.Id, AlbumId = t.AlbumId ?? Guid.Empty }).ToListAsync(ct))
                .ToDictionary(t => t.Id, t => t.AlbumId);
            var albumIds = albumOfTrack.Values.Distinct().ToList();
            var artistOfAlbum = await db.Albums.AsNoTracking().Where(a => albumIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.ArtistId, ct);

            return new TargetLocator(parts, seasonOfEpisode, showOfSeason, albumOfTrack, artistOfAlbum);
        }

        public Guid? PartId(string file) => _parts.TryGetValue(file, out var part) ? part.PartId : null;

        public Guid? Find(MediaEditRecord record)
        {
            if (record.FilePath == null || !_parts.TryGetValue(record.FilePath, out var part)) return null;
            var item = part.ItemId;

            return record.FileLeadsTo switch
            {
                MediaEditFileLeadsTo.Item => item,
                MediaEditFileLeadsTo.SeasonOfEpisode => Lookup(_seasonOfEpisode, item),
                MediaEditFileLeadsTo.ShowOfEpisode => Lookup(_seasonOfEpisode, item) is Guid season ? Lookup(_showOfSeason, season) : null,
                MediaEditFileLeadsTo.AlbumOfTrack => Lookup(_albumOfTrack, item),
                MediaEditFileLeadsTo.ArtistOfTrack => Lookup(_albumOfTrack, item) is Guid album ? Lookup(_artistOfAlbum, album) : null,
                _ => null
            };
        }

        private static Guid? Lookup(Dictionary<Guid, Guid> map, Guid key) => map.TryGetValue(key, out var value) ? value : null;
    }
}
