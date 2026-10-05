using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Iptv;
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
