using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Domain.Entities.Discovery;
using Vora.Domain.Entities.Requests;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups.Sections;

public sealed class DiscoveryRowsBackupSection : EntityTableBackupSection<DiscoveryRowConfig>
{
    public DiscoveryRowsBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "discovery.rows";
    public override string DisplayName => "Discovery Rows";
    public override BackupSectionGroup Group => BackupSectionGroup.Discovery;
    protected override DbSet<DiscoveryRowConfig> Set(VoraDbContext db) => db.DiscoveryRowConfigs;
}

public sealed class RequestServersBackupSection : EntityTableBackupSection<RequestServer>
{
    public RequestServersBackupSection(VoraDbContext db) : base(db) { }
    public override string Key => "discovery.request-servers";
    public override string DisplayName => "Request Servers (Radarr/Sonarr)";
    public override BackupSectionGroup Group => BackupSectionGroup.Discovery;
    protected override DbSet<RequestServer> Set(VoraDbContext db) => db.RequestServers;

    protected override async Task ReleaseReferencesAsync(List<RequestServer> removed, CancellationToken ct)
    {
        var removedIds = removed.Select(s => s.Id).ToList();
        var assigned = await Db.MediaRequests
            .Where(r => r.AssignedServerId != null && removedIds.Contains(r.AssignedServerId.Value))
            .ToListAsync(ct);
        foreach (var request in assigned)
        {
            request.AssignedServerId = null;
        }
    }
}
