using System.IO.Compression;
using System.Text.Json;

namespace Vora.Application.Backups;

public sealed class ZipBackupWriter : IBackupWriter, IDisposable
{
    private readonly ZipArchive _archive;
    private long _currentSectionBytes;
    private int _currentSectionRows;

    public ZipBackupWriter(Stream output)
    {
        _archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
    }

    public void BeginSection(string sectionKey)
    {
        _currentSectionBytes = 0;
        _currentSectionRows = 0;
    }

    public void EndSection() { }

    public Task<long> GetSectionSizeAsync(CancellationToken ct) => Task.FromResult(_currentSectionBytes);

    public int GetSectionRowCount() => _currentSectionRows;

    public async Task WriteJsonAsync<T>(string path, T payload, CancellationToken ct)
    {
        var entry = _archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BackupJson.Options);
        await stream.WriteAsync(bytes, ct);
        _currentSectionBytes += bytes.Length;
        _currentSectionRows += BackupJson.CountRows(path, payload);
    }

    public async Task WriteBytesAsync(string path, byte[] payload, CancellationToken ct)
    {
        var entry = _archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(payload, ct);
        _currentSectionBytes += payload.Length;
    }

    public async Task WriteManifestAsync(BackupManifest manifest, CancellationToken ct)
    {
        var entry = _archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BackupJson.ManifestOptions);
        await stream.WriteAsync(bytes, ct);
    }

    public void Dispose() => _archive.Dispose();
}
