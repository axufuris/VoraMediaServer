namespace Vora.Application.Backups;

public interface IBackupSection
{
    string Key { get; }
    string DisplayName { get; }
    BackupSectionGroup Group { get; }
    bool RequiresExplicitConfirm { get; }
    string? DestructiveWarning { get; }

    Task WriteAsync(IBackupWriter writer, CancellationToken ct);
    Task<BackupSectionImportResult> ReadAsync(IBackupReader reader, CancellationToken ct);
}

public interface IBackupWriter
{
    Task WriteJsonAsync<T>(string path, T payload, CancellationToken ct);
    Task WriteBytesAsync(string path, byte[] payload, CancellationToken ct);
    Task<long> GetSectionSizeAsync(CancellationToken ct);
    void BeginSection(string sectionKey);
    void EndSection();
}

public interface IBackupReader
{
    Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct);
    Task<byte[]?> ReadBytesAsync(string path, CancellationToken ct);
    void BeginSection(string sectionKey);
    void EndSection();
}
