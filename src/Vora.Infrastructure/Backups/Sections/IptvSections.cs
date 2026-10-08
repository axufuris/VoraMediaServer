using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class IptvPlaylistsBackupSection : EntityTableBackupSection<IptvPlaylist>
{
    public IptvPlaylistsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "iptv.playlists";
    public override string DisplayName => "IPTV Playlists";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvPlaylist> Set(VoraDbContext db) => db.IptvPlaylists;
}

public sealed record IptvChannelSetting(Guid PlaylistId, string ExternalChannelId, bool IsHiddenByAdmin, IptvChannelKind Kind, bool KindOverriddenByAdmin);

public sealed class IptvChannelSettingsBackupSection : IBackupSection
{
    private static readonly BackupRowNoun Noun = new("channel setting", "channel settings");

    private readonly VoraDbContext _db;

    public IptvChannelSettingsBackupSection(VoraDbContext db)
    {
        _db = db;
    }

    public string Key => "iptv.channel-settings";
    public string DisplayName => "IPTV Channel Settings";
    public BackupSectionGroup Group => BackupSectionGroup.Iptv;
    public bool RequiresExplicitConfirm => false;
    public bool CanGrowLarge => false;
    public string? DestructiveWarning =>
        "Applies the hidden channels and TV or radio choices in the backup. Channels a playlist hasn't loaded yet are skipped; refresh the playlist, then restore this again.";

    public async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var settings = await _db.IptvChannels
            .AsNoTracking()
            .Where(c => c.IsHiddenByAdmin || c.KindOverriddenByAdmin)
            .Select(c => new IptvChannelSetting(c.PlaylistId, c.ExternalChannelId, c.IsHiddenByAdmin, c.Kind, c.KindOverriddenByAdmin))
            .ToListAsync(ct);
        await writer.WriteJsonAsync($"{Key}/channels.json", settings, ct);
    }

    public async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var settings = await reader.ReadJsonAsync<List<IptvChannelSetting>>($"{Key}/channels.json", ct);
        if (settings == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var playlistIds = settings.Select(s => s.PlaylistId).Distinct().ToList();
        var knownPlaylists = await _db.IptvPlaylists.Where(p => playlistIds.Contains(p.Id)).Select(p => p.Id).ToHashSetAsync(ct);
        var channels = (await _db.IptvChannels.Where(c => playlistIds.Contains(c.PlaylistId)).ToListAsync(ct))
            .GroupBy(c => (c.PlaylistId, c.ExternalChannelId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var applied = 0;
        foreach (var setting in settings)
        {
            if (!channels.TryGetValue((setting.PlaylistId, setting.ExternalChannelId), out var matches))
            {
                tally.Skip(Noun, knownPlaylists.Contains(setting.PlaylistId) ? BackupSkipReason.MissingChannel : BackupSkipReason.MissingIptvPlaylist);
                continue;
            }

            foreach (var channel in matches)
            {
                channel.IsHiddenByAdmin = setting.IsHiddenByAdmin;
                if (setting.KindOverriddenByAdmin)
                {
                    channel.Kind = setting.Kind;
                    channel.KindOverriddenByAdmin = true;
                }
            }
            applied++;
        }

        await _db.SaveChangesAsync(ct);
        return tally.ToResult(applied);
    }
}

public sealed class DvrRecordingsBackupSection : EntityTableBackupSection<IptvRecordingSession>
{
    private static readonly BackupRowNoun Noun = new("recording", "recordings");

    public DvrRecordingsBackupSection(VoraDbContext db) : base(db) { }

    public override string Key => "iptv.recordings";
    public override string DisplayName => "DVR Recordings";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    public override string? DestructiveWarning =>
        "Adds back finished recordings whose files are still on this server. The recordings themselves aren't in the backup, and recordings already here are kept.";
    protected override DbSet<IptvRecordingSession> Set(VoraDbContext db) => db.IptvRecordingSessions;

    protected override IQueryable<IptvRecordingSession> Query(VoraDbContext db) =>
        Set(db).AsNoTracking().Where(s => s.Status == IptvRecordingSessionStatus.Completed && s.OutputFilePath != null);

    public override async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var rows = await reader.ReadJsonAsync<List<IptvRecordingSession>>($"{Key}/rows.json", ct);
        if (rows == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var scheduleIds = await Db.IptvRecordingSchedules.Select(s => s.Id).ToHashSetAsync(ct);
        var restorable = new List<IptvRecordingSession>();
        foreach (var row in rows)
        {
            if (!scheduleIds.Contains(row.ScheduleId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingRecordingSchedule);
                continue;
            }
            if (string.IsNullOrWhiteSpace(row.OutputFilePath) || !File.Exists(row.OutputFilePath))
            {
                tally.Skip(Noun, BackupSkipReason.MissingFile);
                continue;
            }
            restorable.Add(row);
        }

        var ids = restorable.Select(r => r.Id).ToList();
        await BackupTableSync.ReplaceAsync(Db, Db.IptvRecordingSessions.Where(s => ids.Contains(s.Id)), restorable, ct);
        return tally.ToResult(restorable.Count);
    }
}

public sealed class IptvEpgSourcesBackupSection : EntityTableBackupSection<IptvEpgSource>
{
    public IptvEpgSourcesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "iptv.epg-sources";
    public override string DisplayName => "IPTV EPG Sources";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvEpgSource> Set(VoraDbContext db) => db.IptvEpgSources;
}

public sealed class IptvTunerProfilesBackupSection : EntityTableBackupSection<IptvTunerProfile>
{
    private static readonly BackupRowNoun Noun = new("tuner profile", "tuner profiles");

    public IptvTunerProfilesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "iptv.tuner-profiles";
    public override string DisplayName => "IPTV Tuner Profiles";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvTunerProfile> Set(VoraDbContext db) => db.IptvTunerProfiles;

    protected override async Task<List<IptvTunerProfile>> PrepareRowsAsync(IBackupReader reader, List<IptvTunerProfile> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var playlistIds = await Db.IptvPlaylists.Select(p => p.Id).ToHashSetAsync(ct);
        var restorable = new List<IptvTunerProfile>();
        foreach (var row in rows)
        {
            if (!playlistIds.Contains(row.PlaylistId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingIptvPlaylist);
                continue;
            }
            restorable.Add(new IptvTunerProfile { Id = row.Id, PlaylistId = row.PlaylistId, MaxConcurrentStreams = row.MaxConcurrentStreams });
        }
        return restorable.GroupBy(r => r.PlaylistId).Select(g => g.First()).ToList();
    }
}

public sealed class IptvRecordingSchedulesBackupSection : EntityTableBackupSection<IptvRecordingSchedule>
{
    private static readonly BackupRowNoun Noun = new("recording schedule", "recording schedules");

    private readonly BackupReferenceMapper _references;

    public IptvRecordingSchedulesBackupSection(VoraDbContext db, BackupReferenceMapper references) : base(db)
    {
        _references = references;
    }

    public override string Key => "iptv.recording-schedules";
    public override string DisplayName => "IPTV Recording Schedules";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvRecordingSchedule> Set(VoraDbContext db) => db.IptvRecordingSchedules;

    protected override Task WriteReferencesAsync(IBackupWriter writer, List<IptvRecordingSchedule> rows, CancellationToken ct) =>
        _references.WriteIdentitiesAsync(writer, Key, References(rows), ct);

    protected override async Task<List<IptvRecordingSchedule>> PrepareRowsAsync(IBackupReader reader, List<IptvRecordingSchedule> rows, BackupSkipTally tally, CancellationToken ct)
    {
        var resolver = await _references.ReadResolverAsync(reader, Key, References(rows), ct);
        var userIds = await Db.Users.Select(u => u.Id).ToHashSetAsync(ct);
        var profileIds = await Db.UserProfiles.Select(p => p.Id).ToHashSetAsync(ct);

        var restorable = new List<IptvRecordingSchedule>();
        foreach (var row in rows)
        {
            if (!userIds.Contains(row.UserId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingUser);
                continue;
            }
            if (!profileIds.Contains(row.ProfileId))
            {
                tally.Skip(Noun, BackupSkipReason.MissingProfile);
                continue;
            }

            var channelId = resolver.Resolve(BackupItemKind.Channel, row.ChannelId);
            if (channelId == null)
            {
                tally.Skip(Noun, BackupSkipReason.MissingChannel);
                continue;
            }

            restorable.Add(new IptvRecordingSchedule
            {
                Id = row.Id,
                Title = row.Title,
                ProgramId = row.ProgramId,
                IsSeriesRecording = row.IsSeriesRecording,
                KeepMaxEpisodes = row.KeepMaxEpisodes,
                DeleteAfterWatching = row.DeleteAfterWatching,
                IsActive = row.IsActive,
                CreatedAt = row.CreatedAt,
                UserId = row.UserId,
                ProfileId = row.ProfileId,
                ChannelId = channelId.Value
            });
        }
        return restorable;
    }

    private static BackupItemReferences References(List<IptvRecordingSchedule> rows) =>
        new BackupItemReferences().AddRange(BackupItemKind.Channel, rows.Select(r => r.ChannelId));
}
