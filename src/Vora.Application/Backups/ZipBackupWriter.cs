using System.IO.Compression;
using System.Text.Json;

namespace Vora.Application.Backups;

public sealed class ZipBackupWriter : IBackupWriter, IDisposable
{
    private readonly ZipArchive _archive;
    private string _currentSection = string.Empty;
    private long _currentSectionBytes;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ZipBackupWriter(Stream output)
    {
        _archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
    }

    public void BeginSection(string sectionKey)
    {
        _currentSection = sectionKey;
        _currentSectionBytes = 0;
    }

    public void EndSection() => _currentSection = string.Empty;

    public Task<long> GetSectionSizeAsync(CancellationToken ct) => Task.FromResult(_currentSectionBytes);

    public async Task WriteJsonAsync<T>(string path, T payload, CancellationToken ct)
    {
        var entry = _archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await stream.WriteAsync(bytes, ct);
        _currentSectionBytes += bytes.Length;
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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await stream.WriteAsync(bytes, ct);
    }

    public void Dispose() => _archive.Dispose();
}
