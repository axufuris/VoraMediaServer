using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Vora.Application.Backups;

public sealed class CompressedSizeBackupWriter : IBackupWriter
{
    private const int ZipHeaderBytesPerEntry = 30 + 46;
    public const int ZipEndOfArchiveBytes = 22;

    private long _currentSectionBytes;
    private int _currentSectionRows;

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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BackupJson.Options);
        _currentSectionBytes += await MeasureEntryAsync(path, bytes, ct);
        _currentSectionRows += BackupJson.CountRows(path, payload);
    }

    public async Task WriteBytesAsync(string path, byte[] payload, CancellationToken ct)
    {
        _currentSectionBytes += await MeasureEntryAsync(path, payload, ct);
    }

    public static async Task<long> MeasureEntryAsync(string path, byte[] content, CancellationToken ct)
    {
        var counter = new ByteCountingStream();
        await using (var deflate = new DeflateStream(counter, CompressionLevel.Optimal, leaveOpen: true))
        {
            await deflate.WriteAsync(content, ct);
        }

        return counter.BytesWritten + ZipHeaderBytesPerEntry + 2L * Encoding.UTF8.GetByteCount(path);
    }

    private sealed class ByteCountingStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            BytesWritten += count;
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}
