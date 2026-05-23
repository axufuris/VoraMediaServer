using Vora.Application.Backups.ViewModels;

namespace Vora.Application.Backups;

public interface IBackupManager
{
    Task<BackupSummaryVM> CreateBackupAsync(string reason, CancellationToken ct = default);
    Task<List<BackupSummaryVM>> ListBackupsAsync(CancellationToken ct = default);
    Task<BackupManifestVM?> GetManifestAsync(string fileName, CancellationToken ct = default);
    Task<RestoreBackupResult> RestoreBackupAsync(string fileName, RestoreBackupRequest request, Guid? currentAdminUserId, CancellationToken ct = default);
    Task DeleteBackupAsync(string fileName, CancellationToken ct = default);
    Task<Stream> OpenBackupStreamAsync(string fileName, CancellationToken ct = default);
    Task<BackupSummaryVM> UploadBackupAsync(Stream input, string suggestedFileName, CancellationToken ct = default);
    Task<List<AvailableSectionVM>> GetAvailableSectionsAsync(CancellationToken ct = default);
    Task<string> GetEffectiveDirectoryAsync(CancellationToken ct = default);
}
