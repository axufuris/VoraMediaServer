using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vora.Application.Analysis;
using Vora.Application.Logging.ViewModels;

namespace Vora.Application.Logging;

public sealed class LogBroadcastHostedService : IHostedService, IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);
    private const int MaxBatchSize = 200;

    private readonly ILogBuffer _buffer;
    private readonly IServiceProvider _services;
    private readonly object _lock = new();
    private readonly List<LogEntryVM> _pending = new();
    private Timer? _timer;
    private bool _running;

    public LogBroadcastHostedService(ILogBuffer buffer, IServiceProvider services)
    {
        _buffer = buffer;
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _running = true;
        _buffer.EntryAppended += OnEntryAppended;
        _timer = new Timer(_ => FlushAsync().ConfigureAwait(false), null, FlushInterval, FlushInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _running = false;
        _buffer.EntryAppended -= OnEntryAppended;
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    private void OnEntryAppended(LogEntry entry)
    {
        if (!_running) return;
        lock (_lock)
        {
            _pending.Add(new LogEntryVM
            {
                Id = entry.Id,
                TimestampUtc = entry.TimestampUtc,
                Level = entry.Level,
                Category = entry.Category,
                EventId = entry.EventId,
                Message = entry.Message,
                Exception = entry.Exception
            });
            if (_pending.Count >= MaxBatchSize)
            {
                _ = FlushAsync();
            }
        }
    }

    private async Task FlushAsync()
    {
        List<LogEntryVM> batch;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            batch = new List<LogEntryVM>(_pending);
            _pending.Clear();
        }

        try
        {
            using var scope = _services.CreateScope();
            var notifier = scope.ServiceProvider.GetService(typeof(IClientNotifier)) as IClientNotifier;
            if (notifier != null)
            {
                await notifier.NotifyLogEntriesAsync(batch);
            }
        }
        catch
        {
        }
    }

    public void Dispose() => _timer?.Dispose();
}
