using System.IO.Compression;
using System.Text.Json;

namespace Vora.Application.Backups;

public sealed class ZipBackupReader : IBackupReader, IDisposable
{
    private readonly ZipArchive _archive;

    public ZipBackupReader(Stream input)
    {
        _archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
    }

    public void BeginSection(string sectionKey) { }
    public void EndSection() { }

    public async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        var entry = _archive.GetEntry(path);
        if (entry == null) return default;
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: ct);
    }

    public async Task<byte[]?> ReadBytesAsync(string path, CancellationToken ct)
    {
        var entry = _archive.GetEntry(path);
        if (entry == null) return null;
        await using var stream = entry.Open();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public BackupManifest? ReadManifest()
    {
        var entry = _archive.GetEntry("manifest.json");
        if (entry == null) return null;
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<BackupManifest>(stream);
    }

    public void Dispose() => _archive.Dispose();
}
