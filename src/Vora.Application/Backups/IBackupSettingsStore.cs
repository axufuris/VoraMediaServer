namespace Vora.Application.Backups;

public interface IBackupSettingsStore
{
    Task<BackupSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(BackupSettings settings, CancellationToken ct = default);
}
