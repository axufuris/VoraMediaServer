using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Email;
using Vora.Domain.Entities.Posters;
using Vora.Domain.Entities.Templates;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class ClientTemplateSchedulesBackupSection : EntityTableBackupSection<ClientTemplateSchedule>
{
    public ClientTemplateSchedulesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "templates.client-schedules";
    public override string DisplayName => "Client Template Schedules";
    public override BackupSectionGroup Group => BackupSectionGroup.Templates;
    protected override DbSet<ClientTemplateSchedule> Set(VoraDbContext db) => db.ClientTemplateSchedules;
}

public sealed class EmailTemplatesBackupSection : EntityTableBackupSection<EmailTemplate>
{
    public EmailTemplatesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "templates.email";
    public override string DisplayName => "Email Templates";
    public override BackupSectionGroup Group => BackupSectionGroup.Templates;
    protected override DbSet<EmailTemplate> Set(VoraDbContext db) => db.EmailTemplates;
}

public sealed class OverlayTemplatesBackupSection : EntityTableBackupSection<OverlayTemplate>
{
    public OverlayTemplatesBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "templates.overlay";
    public override string DisplayName => "Overlay Templates";
    public override BackupSectionGroup Group => BackupSectionGroup.Templates;
    protected override DbSet<OverlayTemplate> Set(VoraDbContext db) => db.OverlayTemplates;
}
