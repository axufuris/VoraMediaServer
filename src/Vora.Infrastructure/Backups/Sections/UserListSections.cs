using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Playlists;
using Vora.Domain.Entities.Requests;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class PlaylistsBackupSection : IBackupSection
{
    private static readonly BackupRowNoun PlaylistNoun = new("playlist", "playlists");
    private static readonly BackupRowNoun EntryNoun = new("playlist entry", "playlist entries");
    private static readonly BackupRowNoun SmartNoun = new("smart playlist", "smart playlists");

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public PlaylistsBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "users.playlists";
    public string DisplayName => "Playlists";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Replaces every profile's playlists and smart playlists. Uploaded playlist covers are not in the backup; playlists without one build their cover from their first items.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var items = await _db.PlaylistItems.AsNoTracking().ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/playlists.json", await _db.Playlists.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/items.json", items, ct);
        await writer.WriteJsonAsync($"{Key}/smart-playlists.json", await _db.SmartPlaylists.AsNoTracking().ToListAsync(ct), ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(items), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var playlists = await reader.ReadJsonAsync<List<Playlist>>($"{Key}/playlists.json", ct);
        var items = await reader.ReadJsonAsync<List<PlaylistItem>>($"{Key}/items.json", ct) ?? new();
        var smartPlaylists = await reader.ReadJsonAsync<List<SmartPlaylist>>($"{Key}/smart-playlists.json", ct);

        var tally = new BackupSkipTally();
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var imported = 0;

        if (playlists != null)
        {
            var resolver = await _references.ReadResolverAsync(reader, Key, References(items), ct);

            var restoredPlaylists = new List<Playlist>();
            foreach (var playlist in playlists)
            {
                if (!profileIds.Contains(playlist.ProfileId))
                {
                    tally.Skip(PlaylistNoun, BackupSkipReason.MissingProfile);
                    continue;
                }
                playlist.Items = new List<PlaylistItem>();
                restoredPlaylists.Add(playlist);
            }

            var restoredIds = restoredPlaylists.Select(p => p.Id).ToHashSet();
            var restoredItems = new List<PlaylistItem>();
            foreach (var playlistItems in items.GroupBy(i => i.PlaylistId))
            {
                if (!restoredIds.Contains(playlistItems.Key))
                {
                    tally.Skip(EntryNoun, BackupSkipReason.MissingProfile, playlistItems.Count());
                    continue;
                }

                var order = 1;
                foreach (var item in playlistItems.OrderBy(i => i.Order).ThenBy(i => i.AddedAt))
                {
                    var mediaItemId = resolver.Resolve(BackupItemKind.MediaItem, item.MediaItemId);
                    if (mediaItemId == null)
                    {
                        tally.Skip(EntryNoun, BackupSkipReason.MissingItem);
                        continue;
                    }
                    item.MediaItemId = mediaItemId.Value;
                    item.Order = order++;
                    restoredItems.Add(item);
                }
            }

            await BackupTableSync.ReplaceAsync(_db, _db.Playlists, restoredPlaylists, ct);
            await BackupTableSync.ReplaceAsync(_db, _db.PlaylistItems, restoredItems, ct);
            imported += restoredPlaylists.Count + restoredItems.Count;
        }

        if (smartPlaylists != null)
        {
            var restorable = smartPlaylists.Where(p => profileIds.Contains(p.ProfileId)).ToList();
            tally.Skip(SmartNoun, BackupSkipReason.MissingProfile, smartPlaylists.Count - restorable.Count);
            await BackupTableSync.ReplaceAsync(_db, _db.SmartPlaylists, restorable, ct);
            imported += restorable.Count;
        }

        return tally.ToResult(imported);
    }

    private static BackupItemReferences References(List<PlaylistItem> items) =>
        new BackupItemReferences().AddRange(BackupItemKind.MediaItem, items.Select(i => i.MediaItemId));
}

public sealed class WatchlistsBackupSection : EntityTableBackupSection<UserWatchlistItem>
{
    private static readonly BackupRowNoun Noun = new("watchlist entry", "watchlist entries");

    private readonly BackupReferenceMapper _references;

    public WatchlistsBackupSection(VoraDbContext db, BackupReferenceMapper references) : base(db)
    {
        _references = references;
    }

    public override string Key => "users.watchlists";
    public override string DisplayName => "Watchlists";
    public override BackupSectionGroup Group => BackupSectionGroup.UserData;
    public override bool RequiresExplicitConfirm => true;
    public override string? DestructiveWarning => "Replaces every profile's watchlist.";
    protected override DbSet<UserWatchlistItem> Set(VoraDbContext db) => db.UserWatchlistItems;

    protected override Task WriteReferencesAsync(IBackupWriter writer, List<UserWatchlistItem> rows, CancellationToken ct) =>
        _references.WriteIdentitiesAsync(writer, Key, References(rows), ct);

    protected override async Task<List<UserWatchlistItem>> PrepareRowsAsync(IBackupReader reader, List<UserWatchlistItem> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var resolver = await _references.ReadResolverAsync(reader, Key, References(rows), ct);
        var profileIds = await Db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);

        var restorable = new List<UserWatchlistItem>();
        foreach (var row in rows)
        {
            if (!profileIds.Contains(row.ProfileId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingProfile);
                continue;
            }

            if (row.MediaItemId.HasValue)
            {
                var mediaItemId = resolver.Resolve(BackupItemKind.MediaItem, row.MediaItemId.Value);
                if (mediaItemId == null && string.IsNullOrWhiteSpace(row.ExternalId))
                {
                    tally.Skip(Noun, BackupSkipReason.MissingItem);
                    continue;
                }
                row.MediaItemId = mediaItemId;
            }

            restorable.Add(row);
        }

        return BackupTableSync.KeepOnePer(restorable, r => (r.ProfileId, r.ExternalId, r.ProviderId), r => r.AddedAt, tally, Noun);
    }

    private static BackupItemReferences References(List<UserWatchlistItem> rows) =>
        new BackupItemReferences().AddRange(BackupItemKind.MediaItem, rows.Select(r => r.MediaItemId).OfType<Guid>());
}

public sealed class StationsBackupSection : EntityTableBackupSection<Station>
{
    private static readonly BackupRowNoun Noun = new("music station", "music stations");

    private readonly BackupReferenceMapper _references;

    public StationsBackupSection(VoraDbContext db, BackupReferenceMapper references) : base(db)
    {
        _references = references;
    }

    public override string Key => "users.stations";
    public override string DisplayName => "Music Stations";
    public override BackupSectionGroup Group => BackupSectionGroup.UserData;
    public override bool RequiresExplicitConfirm => true;
    public override string? DestructiveWarning => "Replaces every profile's music stations.";
    protected override DbSet<Station> Set(VoraDbContext db) => db.Stations;

    protected override Task WriteReferencesAsync(IBackupWriter writer, List<Station> rows, CancellationToken ct) =>
        _references.WriteIdentitiesAsync(writer, Key, References(rows), ct);

    protected override async Task<List<Station>> PrepareRowsAsync(IBackupReader reader, List<Station> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var resolver = await _references.ReadResolverAsync(reader, Key, References(rows), ct);
        var profileIds = await Db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);

        var restorable = new List<Station>();
        foreach (var row in rows)
        {
            if (!profileIds.Contains(row.ProfileId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingProfile);
                continue;
            }

            var artistId = resolver.Resolve(BackupItemKind.Artist, row.SeedArtistId);
            var trackId = resolver.Resolve(BackupItemKind.MediaItem, row.SeedTrackId);
            var seedMissing = row.SeedKind switch
            {
                StationSeedKind.Artist => artistId == null,
                StationSeedKind.Track => trackId == null,
                _ => false
            };
            if (seedMissing)
            {
                tally.Skip(Noun, BackupSkipReason.MissingItem);
                continue;
            }

            row.SeedArtistId = artistId;
            row.SeedTrackId = trackId;
            restorable.Add(row);
        }
        return restorable;
    }

    private static BackupItemReferences References(List<Station> rows) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.Artist, rows.Select(r => r.SeedArtistId).OfType<Guid>())
            .AddRange(BackupItemKind.MediaItem, rows.Select(r => r.SeedTrackId).OfType<Guid>());
}

public sealed class MediaRequestsBackupSection : IBackupSection
{
    private static readonly BackupRowNoun RequesterNoun = new("requester", "requesters");

    private readonly VoraDbContext _db;

    public MediaRequestsBackupSection(VoraDbContext db)
    {
        _db = db;
    }

    public string Key => "users.requests";
    public string DisplayName => "Media Requests";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning => "Replaces every media request and who asked for it.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/requests.json", await _db.MediaRequests.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/requesters.json", await _db.MediaRequestUsers.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var requests = await reader.ReadJsonAsync<List<MediaRequest>>($"{Key}/requests.json", ct);
        if (requests == null) return new BackupSectionImportResult();
        var requesters = await reader.ReadJsonAsync<List<MediaRequestUser>>($"{Key}/requesters.json", ct) ?? new();

        var tally = new BackupSkipTally();
        var serverIds = await _db.RequestServers.Select(s => s.Id).ToHashSetAsync(ct);
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);

        foreach (var request in requests)
        {
            if (request.AssignedServerId.HasValue && !serverIds.Contains(request.AssignedServerId.Value))
            {
                request.AssignedServerId = null;
            }
            request.AssignedServer = null;
            request.Requesters = new List<MediaRequestUser>();
        }

        var requestIds = requests.Select(r => r.Id).ToHashSet();
        var restoredRequesters = requesters.Where(r => requestIds.Contains(r.RequestId) && profileIds.Contains(r.ProfileId)).ToList();
        tally.Skip(RequesterNoun, BackupSkipReason.MissingProfile, requesters.Count - restoredRequesters.Count);

        await BackupTableSync.ReplaceAsync(_db, _db.MediaRequests, requests, ct);
        await BackupTableSync.ReplaceAsync(_db, _db.MediaRequestUsers, restoredRequesters, ct);

        return tally.ToResult(requests.Count + restoredRequesters.Count);
    }
}
