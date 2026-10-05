using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class UsersAndProfilesBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public UsersAndProfilesBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "users.profiles";
    public string DisplayName => "User Accounts & Profiles";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Replaces all user accounts, profiles, and profile access schedules. Accounts and profiles that are not in the backup are deleted along with their history. Account passwords and refresh tokens revert to the values captured at backup time. If your admin account is not in the backup, restoring will lock you out unless you also acknowledge admin loss.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking().ToListAsync(ct);
        var profiles = await _db.UserProfiles.AsNoTracking().ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/users.json", users, ct);
        await writer.WriteJsonAsync($"{Key}/profiles.json", profiles, ct);
        await writer.WriteJsonAsync($"{Key}/access-schedules.json", await _db.ProfileAccessSchedules.AsNoTracking().ToListAsync(ct), ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(users, profiles), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var users = await reader.ReadJsonAsync<List<User>>($"{Key}/users.json", ct) ?? new();
        var profiles = await reader.ReadJsonAsync<List<UserProfile>>($"{Key}/profiles.json", ct) ?? new();
        var schedules = await reader.ReadJsonAsync<List<ProfileAccessSchedule>>($"{Key}/access-schedules.json", ct) ?? new();

        var resolver = await _references.ReadResolverAsync(reader, Key, References(users, profiles), ct);
        foreach (var user in users)
        {
            user.AllowedLibraryIds = RemapAllowList(user.AllowedLibraryIds, resolver);
        }
        foreach (var profile in profiles)
        {
            profile.AllowedLibraryIds = RemapAllowList(profile.AllowedLibraryIds, resolver);
        }

        await BackupTableSync.ReplaceAsync(_db, _db.Users, users, ReleaseRemovedUsersAsync, ct);
        await BackupTableSync.ReplaceAsync(_db, _db.UserProfiles, profiles, ReleaseRemovedProfilesAsync, ct);
        await BackupTableSync.ReplaceAsync(_db, _db.ProfileAccessSchedules, schedules, ct);

        return new BackupSectionImportResult { RowsImported = users.Count + profiles.Count + schedules.Count };
    }

    private async Task ReleaseRemovedUsersAsync(List<User> removed, CancellationToken ct)
    {
        var userIds = removed.Select(u => u.Id).ToList();
        var profileIds = await _db.UserProfiles.Where(p => userIds.Contains(p.UserId)).Select(p => p.Id).ToListAsync(ct);
        await DetachSessionsFromProfilesAsync(profileIds, ct);
    }

    private Task ReleaseRemovedProfilesAsync(List<UserProfile> removed, CancellationToken ct) =>
        DetachSessionsFromProfilesAsync(removed.Select(p => p.Id).ToList(), ct);

    private async Task DetachSessionsFromProfilesAsync(List<Guid> profileIds, CancellationToken ct)
    {
        if (profileIds.Count == 0) return;
        var sessions = await _db.StreamSessions
            .Where(s => s.UserProfileId != null && profileIds.Contains(s.UserProfileId.Value))
            .ToListAsync(ct);
        foreach (var session in sessions)
        {
            session.UserProfileId = null;
        }
    }

    private static List<Guid> RemapAllowList(List<Guid> ids, BackupReferenceResolver resolver) =>
        ids.Select(id => resolver.Resolve(BackupItemKind.Library, id) ?? id).Distinct().ToList();

    private static BackupItemReferences References(List<User> users, List<UserProfile> profiles) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.Library, users.SelectMany(u => u.AllowedLibraryIds))
            .AddRange(BackupItemKind.Library, profiles.SelectMany(p => p.AllowedLibraryIds));
}

public sealed class DevicesBackupSection : IBackupSection
{
    private static readonly BackupRowNoun SettingNoun = new("per-device setting", "per-device settings");

    private readonly VoraDbContext _db;
    public DevicesBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.devices";
    public string DisplayName => "Devices & Per-Device Settings";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Replaces all authorized client devices and per-profile device settings. Devices currently signed in but not in the backup will be deauthorized.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/devices.json", await _db.ClientDevices.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/profile-device-settings.json", await _db.ProfileDeviceSettings.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var devices = await reader.ReadJsonAsync<List<ClientDevice>>($"{Key}/devices.json", ct) ?? new();
        var deviceSettings = await reader.ReadJsonAsync<List<ProfileDeviceSetting>>($"{Key}/profile-device-settings.json", ct) ?? new();

        var tally = new BackupSkipTally();
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var restorableSettings = new List<ProfileDeviceSetting>();
        foreach (var setting in deviceSettings)
        {
            if (!profileIds.Contains(setting.ProfileId))
            {
                tally.Skip(SettingNoun, BackupSkipReason.MissingProfile);
                continue;
            }
            restorableSettings.Add(setting);
        }
        restorableSettings = BackupTableSync.KeepOnePer(restorableSettings, s => (s.ProfileId, s.DeviceId), _ => DateTime.MinValue, tally, SettingNoun);

        var uniqueDevices = devices.GroupBy(d => d.DeviceId, StringComparer.Ordinal).Select(g => g.First()).ToList();
        tally.Skip(new BackupRowNoun("device", "devices"), BackupSkipReason.Duplicate, devices.Count - uniqueDevices.Count);

        await BackupTableSync.ReplaceAsync(_db, _db.ClientDevices, uniqueDevices, ct);
        await BackupTableSync.ReplaceAsync(_db, _db.ProfileDeviceSettings, restorableSettings, ct);

        return tally.ToResult(uniqueDevices.Count + restorableSettings.Count);
    }
}

public sealed class WatchHistoryBackupSection : IBackupSection
{
    private static readonly BackupRowNoun StateNoun = new("watch-history row", "watch-history rows");
    private static readonly BackupRowNoun SessionNoun = new("playback session", "playback sessions");
    private static readonly BackupRowNoun PlayNoun = new("song play", "song plays");
    private static readonly BackupRowNoun ArchiveNoun = new("Media Trash archive row", "Media Trash archive rows");

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public WatchHistoryBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "users.watch-history";
    public string DisplayName => "Watch History";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => true;
    public string? DestructiveWarning =>
        "Replaces playback state (resume positions, played flags), historical playback sessions, song plays and the watch state Media Trash keeps for removed titles. This section can grow large on long-lived servers.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var states = await _db.UserMediaStates.AsNoTracking().ToListAsync(ct);
        var sessions = await _db.StreamSessions.AsNoTracking().ToListAsync(ct);
        var plays = await _db.TrackPlayHistory.AsNoTracking().ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/media-states.json", states, ct);
        await writer.WriteJsonAsync($"{Key}/stream-sessions.json", sessions, ct);
        await writer.WriteJsonAsync($"{Key}/track-play-history.json", plays, ct);
        await writer.WriteJsonAsync($"{Key}/preserved-user-data.json", await _db.PreservedUserMediaData.AsNoTracking().ToListAsync(ct), ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(states, sessions, plays), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var states = await reader.ReadJsonAsync<List<UserMediaState>>($"{Key}/media-states.json", ct);
        var sessions = await reader.ReadJsonAsync<List<StreamSession>>($"{Key}/stream-sessions.json", ct);
        var plays = await reader.ReadJsonAsync<List<TrackPlayHistory>>($"{Key}/track-play-history.json", ct);
        var preserved = await reader.ReadJsonAsync<List<PreservedUserMediaData>>($"{Key}/preserved-user-data.json", ct);

        var tally = new BackupSkipTally();
        var resolver = await _references.ReadResolverAsync(reader, Key, References(states ?? new(), sessions ?? new(), plays ?? new()), ct);
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var imported = 0;

        if (states != null)
        {
            var restorable = new List<UserMediaState>();
            foreach (var state in states)
            {
                if (!profileIds.Contains(state.ProfileId)) { tally.Skip(StateNoun, BackupSkipReason.MissingProfile); continue; }
                var itemId = resolver.Resolve(BackupItemKind.MediaItem, state.MediaItemId);
                if (itemId == null) { tally.Skip(StateNoun, BackupSkipReason.MissingItem); continue; }
                state.MediaItemId = itemId.Value;
                restorable.Add(state);
            }
            restorable = BackupTableSync.KeepOnePer(restorable, s => (s.ProfileId, s.MediaItemId), s => s.LastPlayedAt, tally, StateNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.UserMediaStates, restorable, ct);
            imported += restorable.Count;
        }

        if (sessions != null)
        {
            var userIds = await _db.Users.Select(u => u.Id).ToHashSetAsync(ct);
            var deviceIds = await _db.ClientDevices.Select(d => d.Id).ToHashSetAsync(ct);
            var restorable = new List<StreamSession>();
            foreach (var session in sessions)
            {
                if (!userIds.Contains(session.UserId)) { tally.Skip(SessionNoun, BackupSkipReason.MissingUser); continue; }
                if (session.UserProfileId.HasValue && !profileIds.Contains(session.UserProfileId.Value)) { tally.Skip(SessionNoun, BackupSkipReason.MissingProfile); continue; }
                if (!deviceIds.Contains(session.ClientDeviceId)) { tally.Skip(SessionNoun, BackupSkipReason.MissingDevice); continue; }
                var itemId = resolver.Resolve(BackupItemKind.MediaItem, session.MediaItemId);
                if (itemId == null) { tally.Skip(SessionNoun, BackupSkipReason.MissingItem); continue; }
                session.MediaItemId = itemId.Value;
                restorable.Add(session);
            }
            await BackupTableSync.ReplaceAsync(_db, _db.StreamSessions, restorable, ct);
            imported += restorable.Count;
        }

        if (plays != null)
        {
            var restorable = new List<TrackPlayHistory>();
            foreach (var play in plays)
            {
                if (!profileIds.Contains(play.ProfileId)) { tally.Skip(PlayNoun, BackupSkipReason.MissingProfile); continue; }
                var trackId = resolver.Resolve(BackupItemKind.MediaItem, play.TrackId);
                if (trackId == null) { tally.Skip(PlayNoun, BackupSkipReason.MissingItem); continue; }
                play.TrackId = trackId.Value;
                restorable.Add(play);
            }
            await BackupTableSync.ReplaceAsync(_db, _db.TrackPlayHistory, restorable, ct);
            imported += restorable.Count;
        }

        if (preserved != null)
        {
            var restorable = new List<PreservedUserMediaData>();
            foreach (var archive in preserved)
            {
                if (!profileIds.Contains(archive.ProfileId)) { tally.Skip(ArchiveNoun, BackupSkipReason.MissingProfile); continue; }
                restorable.Add(archive);
            }
            restorable = BackupTableSync.KeepOnePer(restorable, a => (a.ProfileId, a.ContentKey), a => a.ArchivedAt, tally, ArchiveNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.PreservedUserMediaData, restorable, ct);
            imported += restorable.Count;
        }

        return tally.ToResult(imported);
    }

    private static BackupItemReferences References(List<UserMediaState> states, List<StreamSession> sessions, List<TrackPlayHistory> plays) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.MediaItem, states.Select(s => s.MediaItemId))
            .AddRange(BackupItemKind.MediaItem, sessions.Select(s => s.MediaItemId))
            .AddRange(BackupItemKind.MediaItem, plays.Select(p => p.TrackId));
}

public sealed class RatingsBackupSection : IBackupSection
{
    private static readonly BackupRowNoun RatingNoun = new("rating", "ratings");
    private static readonly BackupRowNoun AlbumNoun = new("album rating", "album ratings");
    private static readonly BackupRowNoun ArtistNoun = new("artist rating", "artist ratings");
    private static readonly BackupRowNoun LikeNoun = new("song like", "song likes");

    private readonly VoraDbContext _db;
    private readonly BackupReferenceMapper _references;

    public RatingsBackupSection(VoraDbContext db, BackupReferenceMapper references)
    {
        _db = db;
        _references = references;
    }

    public string Key => "users.ratings";
    public string DisplayName => "Ratings & Likes";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning => "Replaces all media, artist, album ratings and track likes.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var media = await _db.UserMediaRatings.AsNoTracking().ToListAsync(ct);
        var albums = await _db.UserAlbumRatings.AsNoTracking().ToListAsync(ct);
        var artists = await _db.UserArtistRatings.AsNoTracking().ToListAsync(ct);
        var likes = await _db.TrackLikes.AsNoTracking().ToListAsync(ct);

        await writer.WriteJsonAsync($"{Key}/media-ratings.json", media, ct);
        await writer.WriteJsonAsync($"{Key}/album-ratings.json", albums, ct);
        await writer.WriteJsonAsync($"{Key}/artist-ratings.json", artists, ct);
        await writer.WriteJsonAsync($"{Key}/track-likes.json", likes, ct);
        await _references.WriteIdentitiesAsync(writer, Key, References(media, albums, artists, likes), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var media = await reader.ReadJsonAsync<List<UserMediaRating>>($"{Key}/media-ratings.json", ct);
        var albums = await reader.ReadJsonAsync<List<UserAlbumRating>>($"{Key}/album-ratings.json", ct);
        var artists = await reader.ReadJsonAsync<List<UserArtistRating>>($"{Key}/artist-ratings.json", ct);
        var likes = await reader.ReadJsonAsync<List<TrackLike>>($"{Key}/track-likes.json", ct);

        var tally = new BackupSkipTally();
        var resolver = await _references.ReadResolverAsync(reader, Key, References(media ?? new(), albums ?? new(), artists ?? new(), likes ?? new()), ct);
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var imported = 0;

        if (media != null)
        {
            var restorable = Remap(media, r => r.ProfileId, BackupItemKind.MediaItem, r => r.MediaItemId, (r, id) => r.MediaItemId = id, profileIds, resolver, tally, RatingNoun);
            restorable = BackupTableSync.KeepOnePer(restorable, r => (r.ProfileId, r.MediaItemId), r => r.RatedAt, tally, RatingNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.UserMediaRatings, restorable, ct);
            imported += restorable.Count;
        }

        if (albums != null)
        {
            var restorable = Remap(albums, r => r.ProfileId, BackupItemKind.Album, r => r.AlbumId, (r, id) => r.AlbumId = id, profileIds, resolver, tally, AlbumNoun);
            restorable = BackupTableSync.KeepOnePer(restorable, r => (r.ProfileId, r.AlbumId), r => r.RatedAt, tally, AlbumNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.UserAlbumRatings, restorable, ct);
            imported += restorable.Count;
        }

        if (artists != null)
        {
            var restorable = Remap(artists, r => r.ProfileId, BackupItemKind.Artist, r => r.ArtistId, (r, id) => r.ArtistId = id, profileIds, resolver, tally, ArtistNoun);
            restorable = BackupTableSync.KeepOnePer(restorable, r => (r.ProfileId, r.ArtistId), r => r.RatedAt, tally, ArtistNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.UserArtistRatings, restorable, ct);
            imported += restorable.Count;
        }

        if (likes != null)
        {
            var restorable = Remap(likes, l => l.ProfileId, BackupItemKind.MediaItem, l => l.TrackId, (l, id) => l.TrackId = id, profileIds, resolver, tally, LikeNoun);
            restorable = BackupTableSync.KeepOnePer(restorable, l => (l.ProfileId, l.TrackId), l => l.LikedAt, tally, LikeNoun);
            await BackupTableSync.ReplaceAsync(_db, _db.TrackLikes, restorable, ct);
            imported += restorable.Count;
        }

        return tally.ToResult(imported);
    }

    private static List<T> Remap<T>(
        List<T> rows,
        Func<T, Guid> profileId,
        BackupItemKind kind,
        Func<T, Guid> targetId,
        Action<T, Guid> setTargetId,
        HashSet<Guid> profileIds,
        BackupReferenceResolver resolver,
        BackupSkipTally tally,
        BackupRowNoun noun)
    {
        var restorable = new List<T>();
        foreach (var row in rows)
        {
            if (!profileIds.Contains(profileId(row))) { tally.Skip(noun, BackupSkipReason.MissingProfile); continue; }
            var resolved = resolver.Resolve(kind, targetId(row));
            if (resolved == null) { tally.Skip(noun, BackupSkipReason.MissingItem); continue; }
            setTargetId(row, resolved.Value);
            restorable.Add(row);
        }
        return restorable;
    }

    private static BackupItemReferences References(List<UserMediaRating> media, List<UserAlbumRating> albums, List<UserArtistRating> artists, List<TrackLike> likes) =>
        new BackupItemReferences()
            .AddRange(BackupItemKind.MediaItem, media.Select(r => r.MediaItemId))
            .AddRange(BackupItemKind.Album, albums.Select(r => r.AlbumId))
            .AddRange(BackupItemKind.Artist, artists.Select(r => r.ArtistId))
            .AddRange(BackupItemKind.MediaItem, likes.Select(l => l.TrackId));
}

public sealed class ExternalConnectionsBackupSection : EntityTableBackupSection<UserProviderConnection>
{
    private static readonly BackupRowNoun Noun = new("linked account", "linked accounts");

    public ExternalConnectionsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "users.external-connections";
    public override string DisplayName => "External Connections (Trakt, etc.)";
    public override BackupSectionGroup Group => BackupSectionGroup.UserData;
    public override bool RequiresExplicitConfirm => true;
    public override string? DestructiveWarning => "Replaces linked third-party accounts (e.g. Trakt). Existing tokens are overwritten.";
    protected override DbSet<UserProviderConnection> Set(VoraDbContext db) => db.UserProviderConnections;

    protected override async Task<List<UserProviderConnection>> PrepareRowsAsync(IBackupReader reader, List<UserProviderConnection> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var userIds = await Db.Users.Select(u => u.Id).ToHashSetAsync(ct);
        var restorable = rows.Where(r => userIds.Contains(r.UserId)).ToList();
        tally.Skip(Noun, BackupSkipReason.MissingUser, rows.Count - restorable.Count);
        return restorable;
    }
}

public sealed class ChannelFavoritesBackupSection : IBackupSection
{
    private static readonly BackupRowNoun Noun = new("favorite", "favorites");

    private readonly VoraDbContext _db;
    public ChannelFavoritesBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.channel-favorites";
    public string DisplayName => "Live TV & Radio Favorites";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning => "Replaces every profile's favorite Live TV channels and radio stations.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/rows.json", await _db.ProfileChannelFavorites.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var rows = await reader.ReadJsonAsync<List<ProfileChannelFavorite>>($"{Key}/rows.json", ct);
        if (rows == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var profileIds = await _db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);
        var playlistIds = await _db.IptvPlaylists.Select(p => p.Id).ToHashSetAsync(ct);

        var restorable = new List<ProfileChannelFavorite>();
        foreach (var row in rows)
        {
            if (!profileIds.Contains(row.ProfileId)) { tally.Skip(Noun, BackupSkipReason.MissingProfile); continue; }
            if (!playlistIds.Contains(row.PlaylistId)) { tally.Skip(Noun, BackupSkipReason.MissingIptvPlaylist); continue; }
            restorable.Add(new ProfileChannelFavorite { ProfileId = row.ProfileId, PlaylistId = row.PlaylistId, ExternalChannelId = row.ExternalChannelId, AddedAt = row.AddedAt });
        }
        restorable = BackupTableSync.KeepOnePer(restorable, r => (r.ProfileId, r.PlaylistId, r.ExternalChannelId), r => r.AddedAt, tally, Noun);

        await BackupTableSync.ReplaceAsync(_db, _db.ProfileChannelFavorites, restorable, ct);
        return tally.ToResult(restorable.Count);
    }
}
