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
    public IptvTunerProfilesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "iptv.tuner-profiles";
    public override string DisplayName => "IPTV Tuner Profiles";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvTunerProfile> Set(VoraDbContext db) => db.IptvTunerProfiles;
}

public sealed class IptvRecordingSchedulesBackupSection : EntityTableBackupSection<IptvRecordingSchedule>
{
    public IptvRecordingSchedulesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "iptv.recording-schedules";
    public override string DisplayName => "IPTV Recording Schedules";
    public override BackupSectionGroup Group => BackupSectionGroup.Iptv;
    protected override DbSet<IptvRecordingSchedule> Set(VoraDbContext db) => db.IptvRecordingSchedules;
}
