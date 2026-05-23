using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.Streaming;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class UsersAndProfilesBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;
    public UsersAndProfilesBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.profiles";
    public string DisplayName => "User Accounts & Profiles";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public string? DestructiveWarning =>
        "Replaces all user accounts, profiles, and profile access schedules. Account passwords and refresh tokens revert to the values captured at backup time. If your admin account is not in the backup, restoring will lock you out unless you also acknowledge admin loss.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/users.json", await _db.Users.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/profiles.json", await _db.UserProfiles.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/access-schedules.json", await _db.ProfileAccessSchedules.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var users = await reader.ReadJsonAsync<List<User>>($"{Key}/users.json", ct) ?? new();
        var profiles = await reader.ReadJsonAsync<List<UserProfile>>($"{Key}/profiles.json", ct) ?? new();
        var schedules = await reader.ReadJsonAsync<List<ProfileAccessSchedule>>($"{Key}/access-schedules.json", ct) ?? new();

        _db.ProfileAccessSchedules.RemoveRange(await _db.ProfileAccessSchedules.ToListAsync(ct));
        _db.UserProfiles.RemoveRange(await _db.UserProfiles.ToListAsync(ct));
        _db.Users.RemoveRange(await _db.Users.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        await _db.Users.AddRangeAsync(users, ct);
        await _db.UserProfiles.AddRangeAsync(profiles, ct);
        await _db.ProfileAccessSchedules.AddRangeAsync(schedules, ct);
        await _db.SaveChangesAsync(ct);

        return new BackupSectionImportResult { RowsImported = users.Count + profiles.Count + schedules.Count };
    }
}

public sealed class DevicesBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;
    public DevicesBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.devices";
    public string DisplayName => "Devices & Per-Device Settings";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
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

        _db.ProfileDeviceSettings.RemoveRange(await _db.ProfileDeviceSettings.ToListAsync(ct));
        _db.ClientDevices.RemoveRange(await _db.ClientDevices.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        await _db.ClientDevices.AddRangeAsync(devices, ct);
        await _db.ProfileDeviceSettings.AddRangeAsync(deviceSettings, ct);
        await _db.SaveChangesAsync(ct);

        return new BackupSectionImportResult { RowsImported = devices.Count + deviceSettings.Count };
    }
}

public sealed class WatchHistoryBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;
    public WatchHistoryBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.watch-history";
    public string DisplayName => "Watch History";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public string? DestructiveWarning =>
        "Replaces playback state (resume positions, played flags) and historical playback sessions. This section can grow large on long-lived servers.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/media-states.json", await _db.UserMediaStates.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/stream-sessions.json", await _db.StreamSessions.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/track-play-history.json", await _db.TrackPlayHistory.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var states = await reader.ReadJsonAsync<List<UserMediaState>>($"{Key}/media-states.json", ct) ?? new();
        var sessions = await reader.ReadJsonAsync<List<StreamSession>>($"{Key}/stream-sessions.json", ct) ?? new();
        var trackHistory = await reader.ReadJsonAsync<List<TrackPlayHistory>>($"{Key}/track-play-history.json", ct) ?? new();

        _db.UserMediaStates.RemoveRange(await _db.UserMediaStates.ToListAsync(ct));
        _db.StreamSessions.RemoveRange(await _db.StreamSessions.ToListAsync(ct));
        _db.TrackPlayHistory.RemoveRange(await _db.TrackPlayHistory.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        await _db.UserMediaStates.AddRangeAsync(states, ct);
        await _db.StreamSessions.AddRangeAsync(sessions, ct);
        await _db.TrackPlayHistory.AddRangeAsync(trackHistory, ct);
        await _db.SaveChangesAsync(ct);

        return new BackupSectionImportResult { RowsImported = states.Count + sessions.Count + trackHistory.Count };
    }
}

public sealed class RatingsBackupSection : IBackupSection
{
    private readonly VoraDbContext _db;
    public RatingsBackupSection(VoraDbContext db) { _db = db; }

    public string Key => "users.ratings";
    public string DisplayName => "Ratings & Likes";
    public BackupSectionGroup Group => BackupSectionGroup.UserData;
    public bool RequiresExplicitConfirm => true;
    public string? DestructiveWarning => "Replaces all media, artist, album ratings and track likes.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        await writer.WriteJsonAsync($"{Key}/media-ratings.json", await _db.UserMediaRatings.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/album-ratings.json", await _db.UserAlbumRatings.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/artist-ratings.json", await _db.UserArtistRatings.AsNoTracking().ToListAsync(ct), ct);
        await writer.WriteJsonAsync($"{Key}/track-likes.json", await _db.TrackLikes.AsNoTracking().ToListAsync(ct), ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var media = await reader.ReadJsonAsync<List<UserMediaRating>>($"{Key}/media-ratings.json", ct) ?? new();
        var albums = await reader.ReadJsonAsync<List<UserAlbumRating>>($"{Key}/album-ratings.json", ct) ?? new();
        var artists = await reader.ReadJsonAsync<List<UserArtistRating>>($"{Key}/artist-ratings.json", ct) ?? new();
        var likes = await reader.ReadJsonAsync<List<TrackLike>>($"{Key}/track-likes.json", ct) ?? new();

        _db.UserMediaRatings.RemoveRange(await _db.UserMediaRatings.ToListAsync(ct));
        _db.UserAlbumRatings.RemoveRange(await _db.UserAlbumRatings.ToListAsync(ct));
        _db.UserArtistRatings.RemoveRange(await _db.UserArtistRatings.ToListAsync(ct));
        _db.TrackLikes.RemoveRange(await _db.TrackLikes.ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        await _db.UserMediaRatings.AddRangeAsync(media, ct);
        await _db.UserAlbumRatings.AddRangeAsync(albums, ct);
        await _db.UserArtistRatings.AddRangeAsync(artists, ct);
        await _db.TrackLikes.AddRangeAsync(likes, ct);
        await _db.SaveChangesAsync(ct);

        return new BackupSectionImportResult { RowsImported = media.Count + albums.Count + artists.Count + likes.Count };
    }
}

public sealed class ExternalConnectionsBackupSection : EntityTableBackupSection<UserProviderConnection>
{
    public ExternalConnectionsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "users.external-connections";
    public override string DisplayName => "External Connections (Trakt, etc.)";
    public override BackupSectionGroup Group => BackupSectionGroup.UserData;
    public override bool RequiresExplicitConfirm => true;
    public override string? DestructiveWarning => "Replaces linked third-party accounts (e.g. Trakt). Existing tokens are overwritten.";
    protected override DbSet<UserProviderConnection> Set(VoraDbContext db) => db.UserProviderConnections;
}
