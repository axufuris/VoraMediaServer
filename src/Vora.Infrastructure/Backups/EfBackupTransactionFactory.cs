using Microsoft.EntityFrameworkCore.Storage;
using Vora.Application.Backups;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Backups;

public sealed class EfBackupTransactionFactory : IBackupTransactionFactory
{
    private readonly VoraDbContext _db;

    public EfBackupTransactionFactory(VoraDbContext db)
    {
        _db = db;
    }

    public async Task<IBackupTransaction> BeginAsync(CancellationToken ct)
    {
        var tx = await _db.Database.BeginTransactionAsync(ct);
        return new EfBackupTransaction(tx);
    }
}

internal sealed class EfBackupTransaction : IBackupTransaction
{
    private readonly IDbContextTransaction _tx;
    private bool _completed;

    public EfBackupTransaction(IDbContextTransaction tx)
    {
        _tx = tx;
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_completed) return;
        await _tx.CommitAsync(ct);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_completed) return;
        await _tx.RollbackAsync(ct);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try { await _tx.RollbackAsync(); } catch { }
        }
        await _tx.DisposeAsync();
    }
}
