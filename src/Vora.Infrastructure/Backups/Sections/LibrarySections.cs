using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Media;
using Vora.Domain.Entities.SmartLists;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class SmartListsBackupSection : EntityTableBackupSection<SmartList>
{
    public SmartListsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "library.smart-lists";
    public override string DisplayName => "Smart Lists";
    public override BackupSectionGroup Group => BackupSectionGroup.Library;
    protected override DbSet<SmartList> Set(VoraDbContext db) => db.SmartLists;
}

public sealed class DedupeRulesBackupSection : EntityTableBackupSection<MediaDedupeSettings>
{
    public DedupeRulesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "library.dedupe-rules";
    public override string DisplayName => "Dedupe Rules";
    public override BackupSectionGroup Group => BackupSectionGroup.Library;
    protected override DbSet<MediaDedupeSettings> Set(VoraDbContext db) => db.MediaDedupeSettings;
}
