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
    public virtual bool CanGrowLarge => false;
    public virtual string? DestructiveWarning => null;

    protected abstract DbSet<TEntity> Set(VoraDbContext db);
    protected virtual IQueryable<TEntity> Query(VoraDbContext db) => Set(db).AsNoTracking();
    protected virtual IQueryable<TEntity> ReplaceScope(VoraDbContext db) => Set(db);
    protected virtual string RowsFileName => "rows.json";

    public virtual async Task WriteAsync(IBackupWriter writer, CancellationToken ct)
    {
        var rows = await Query(Db).ToListAsync(ct);
        await writer.WriteJsonAsync($"{Key}/{RowsFileName}", rows, ct);
        await WriteReferencesAsync(writer, rows, ct);
    }

    public virtual async Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct)
    {
        var rows = await reader.ReadJsonAsync<List<TEntity>>($"{Key}/{RowsFileName}", ct);
        if (rows == null) return new BackupSectionImportResult();

        var tally = new BackupSkipTally();
        var restorable = await PrepareRowsAsync(reader, rows, tally, ct);
        await BackupTableSync.ReplaceAsync(Db, ReplaceScope(Db), restorable, ReleaseReferencesAsync, ct);
        return tally.ToResult(restorable.Count);
    }

    protected virtual Task ReleaseReferencesAsync(List<TEntity> removed, CancellationToken ct) => Task.CompletedTask;

    protected virtual Task WriteReferencesAsync(IBackupWriter writer, List<TEntity> rows, CancellationToken ct) => Task.CompletedTask;

    protected virtual Task<List<TEntity>> PrepareRowsAsync(IBackupReader reader, List<TEntity> rows, BackupSkipTally tally, CancellationToken ct) =>
        Task.FromResult(rows);
}
