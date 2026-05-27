using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Vora.Application.Logging;

public sealed class LogFileSinkOptions
{
    public string Directory { get; set; } = "logs";
    public int RetentionDays { get; set; } = 14;
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
                EnsureWriter(entry.TimestampUtc);
                if (_writer == null) continue;

                var sb = new StringBuilder(256);
                sb.Append(entry.TimestampUtc.ToString("O"))
                  .Append(' ').Append('[').Append(entry.Level.ToString().ToUpperInvariant()).Append(']')
                  .Append(' ').Append(entry.Category)
                  .Append(": ").Append(entry.Message);

                if (!string.IsNullOrEmpty(entry.Exception))
                {
                    sb.Append(Environment.NewLine).Append(entry.Exception);
                }

                await _writer.WriteLineAsync(sb.ToString());
                await _writer.FlushAsync(ct);
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

    private void EnsureWriter(DateTime utc)
    {
        var date = DateOnly.FromDateTime(utc);
        if (_writer != null && date == _currentDate) return;

        _writer?.Flush();
        _writer?.Dispose();

        _currentDate = date;
        _currentFile = Path.Combine(_options.Directory, $"vora-{date:yyyyMMdd}.log");
        var stream = new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

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
