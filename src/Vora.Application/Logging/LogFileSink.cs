using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Vora.Application.Logging;

public sealed class LogFileSinkOptions
{
    public string Directory { get; set; } = "logs";
    public int RetentionDays { get; set; } = 14;
    // Roll to a new segment file once the current one reaches this size, so a
    // single busy day (a full library scan logs a lot) can't produce one
    // multi-GB file that's impossible to open or search.
    public long MaxBytes { get; set; } = 50 * 1024 * 1024;
}

public sealed class LogFileSink : IHostedService, IDisposable
{
    private readonly Channel<LogEntry> _channel;
    private readonly LogFileSinkOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private string? _currentFile;
    private DateOnly _currentDate;
    private StreamWriter? _writer;
    private long _currentBytes;
    private int _segment;

    public LogFileSink(LogFileSinkOptions options)
    {
        _options = options;
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(20_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(LogEntry entry)
    {
        _channel.Writer.TryWrite(entry);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.Directory);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => RunAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_cts != null)
        {
            try { await _cts.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }
        if (_worker != null)
        {
            try { await _worker; } catch { }
        }
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync(ct))
            {
                var sb = new StringBuilder(256);
                sb.Append(entry.TimestampUtc.ToString("O"))
                  .Append(' ').Append('[').Append(entry.Level.ToString().ToUpperInvariant()).Append(']')
                  .Append(' ').Append(entry.Category)
                  .Append(": ").Append(entry.Message);

                if (!string.IsNullOrEmpty(entry.Exception))
                {
                    sb.Append(Environment.NewLine).Append(entry.Exception);
                }

                var line = sb.ToString();
                var lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;

                EnsureWriter(entry.TimestampUtc, lineBytes);
                if (_writer == null) continue;

                await _writer.WriteLineAsync(line);
                await _writer.FlushAsync(ct);
                _currentBytes += lineBytes;
                TryPrune();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void EnsureWriter(DateTime utc, int incomingBytes)
    {
        var date = DateOnly.FromDateTime(utc);
        var dateChanged = _writer == null || date != _currentDate;
        var sizeExceeded = _writer != null && _currentBytes + incomingBytes > _options.MaxBytes;
        if (_writer != null && !dateChanged && !sizeExceeded) return;

        _writer?.Flush();
        _writer?.Dispose();

        Directory.CreateDirectory(_options.Directory);

        if (dateChanged) { _currentDate = date; _segment = 0; }
        else if (sizeExceeded) { _segment++; }

        _currentFile = SegmentPath(date, _segment);
        // On startup mid-day, skip past any segments already at the cap so we
        // don't reopen and keep growing yesterday's giant file.
        while (dateChanged && File.Exists(_currentFile) && new FileInfo(_currentFile).Length >= _options.MaxBytes)
        {
            _segment++;
            _currentFile = SegmentPath(date, _segment);
        }

        _currentBytes = File.Exists(_currentFile) ? new FileInfo(_currentFile).Length : 0;
        var stream = new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

    private string SegmentPath(DateOnly date, int segment) =>
        Path.Combine(_options.Directory, segment == 0 ? $"vora-{date:yyyyMMdd}.log" : $"vora-{date:yyyyMMdd}.{segment}.log");

    private DateTime _lastPrune = DateTime.MinValue;
    private void TryPrune()
    {
        if ((DateTime.UtcNow - _lastPrune).TotalHours < 6) return;
        _lastPrune = DateTime.UtcNow;
        try
        {
            var cutoff = DateTime.UtcNow.Date.AddDays(-_options.RetentionDays);
            foreach (var file in Directory.EnumerateFiles(_options.Directory, "vora-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var datePart = name.Length >= 13 ? name[5..13] : null;
                if (datePart != null && DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    if (dt < cutoff)
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _writer?.Dispose();
    }
}
