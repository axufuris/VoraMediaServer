using Microsoft.EntityFrameworkCore;
using Vora.Application.Backups;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups;

public abstract class EntityTableBackupSection<TEntity> : IBackupSection where TEntity : class
{
    protected readonly VoraDbContext Db;

    protected EntityTableBackupSection(VoraDbContext db)
    {
        Db = db;
    }

    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public abstract BackupSectionGroup Group { get; }
    public virtual bool RequiresExplicitConfirm => false;
    public virtual string? DestructiveWarning => null;

    protected abstract DbSet<TEntity> Set(VoraDbContext db);
    protected virtual IQueryable<TEntity> Query(VoraDbContext db) => Set(db).AsNoTracking();
    protected virtual string RowsFileName => "rows.json";

    public virtual async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var rows = await Query(Db).ToListAsync(ct);
        await writer.WriteJsonAsync($"{Key}/{RowsFileName}", rows, ct);
    }

    public virtual async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var rows = await reader.ReadJsonAsync<List<TEntity>>($"{Key}/{RowsFileName}", ct);
        if (rows == null) return new BackupSectionImportResult();

        var existing = await Set(Db).ToListAsync(ct);
        Set(Db).RemoveRange(existing);
        await Db.SaveChangesAsync(ct);
        await Set(Db).AddRangeAsync(rows, ct);
        await Db.SaveChangesAsync(ct);

        return new BackupSectionImportResult { RowsImported = rows.Count };
    }
}
