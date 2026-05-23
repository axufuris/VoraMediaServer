namespace Vora.Application.Backups;

public interface IBackupTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}

public interface IBackupTransactionFactory
{
    Task<IBackupTransaction> BeginAsync(CancellationToken ct);
}
