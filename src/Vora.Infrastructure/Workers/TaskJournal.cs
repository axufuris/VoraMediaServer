using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Tasks;
using Vora.Domain.Entities.Tasks;
using Vora.Infrastructure.Persistence;

namespace Vora.Infrastructure.Workers;

public class TaskJournal : BackgroundService, ITaskJournal
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 500;
    private const int NameMaxLength = 512;

    private readonly IServiceProvider _services;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskJournal> _logger;
    private readonly ConcurrentQueue<JournalEntry> _entries = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly Dictionary<Guid, JournalEntry> _unsaved = new();
    private long _sequence = DateTime.UtcNow.Ticks;

    public TaskJournal(IServiceProvider services, IServiceScopeFactory scopeFactory, ILogger<TaskJournal> logger)
    {
        _services = services;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private sealed record JournalEntry(Guid TaskId, string? Name, TaskRecipe? Recipe, long Sequence, DateTime QueuedAt)
    {
        public bool IsCompletion => Recipe == null;
    }

    public void Record(Guid taskId, string name, TaskRecipe recipe) =>
        _entries.Enqueue(new JournalEntry(taskId, name, recipe, Interlocked.Increment(ref _sequence), DateTime.UtcNow));

    public void Complete(Guid taskId) =>
        _entries.Enqueue(new JournalEntry(taskId, null, null, 0, DateTime.UtcNow));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RestoreAsync(stoppingToken);

        using var timer = new PeriodicTimer(FlushInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        List<PendingTask> saved;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
            saved = await db.PendingTasks.AsNoTracking().OrderBy(t => t.Sequence).ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the tasks saved before the last shutdown; they will not be resumed.");
            return;
        }

        if (saved.Count == 0) return;

        _logger.LogInformation("Resuming {Count} task(s) that were queued or running when the server stopped.", saved.Count);
        var queue = _services.GetRequiredService<ITaskQueueManager>();
        foreach (var row in saved)
        {
            Complete(row.Id);
            try
            {
                if (!TaskRecipes.Restore(queue, TaskRecipe.FromJson(row.Kind, row.ArgumentsJson)))
                {
                    _logger.LogWarning("Not resuming {TaskName}: this version has no task of kind {Kind}.", row.Name, row.Kind);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resume {TaskName} ({Kind}).", row.Name, row.Kind);
            }
        }

        await FlushAsync(cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            while (_entries.TryDequeue(out var entry))
            {
                if (!entry.IsCompletion && _unsaved.TryGetValue(entry.TaskId, out var earlier) && !earlier.IsCompletion)
                {
                    entry = entry with { Sequence = earlier.Sequence, QueuedAt = earlier.QueuedAt };
                }
                _unsaved[entry.TaskId] = entry;
            }

            if (_unsaved.Count == 0) return;

            foreach (var chunk in _unsaved.Values.ToList().Chunk(BatchSize))
            {
                await SaveAsync(chunk, cancellationToken);
                foreach (var entry in chunk) _unsaved.Remove(entry.TaskId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not save the task queue; trying again shortly.");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task SaveAsync(IReadOnlyCollection<JournalEntry> entries, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var ids = entries.Select(e => e.TaskId).ToList();
        var existing = await db.PendingTasks.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);

        foreach (var entry in entries)
        {
            existing.TryGetValue(entry.TaskId, out var row);
            if (entry.Recipe == null)
            {
                if (row != null) db.PendingTasks.Remove(row);
                continue;
            }

            if (row == null)
            {
                db.PendingTasks.Add(new PendingTask
                {
                    Id = entry.TaskId,
                    Sequence = entry.Sequence,
                    QueuedAt = entry.QueuedAt,
                    Kind = entry.Recipe.Kind,
                    ArgumentsJson = entry.Recipe.ArgumentsJson,
                    Name = Truncate(entry.Name),
                });
            }
            else
            {
                row.Kind = entry.Recipe.Kind;
                row.ArgumentsJson = entry.Recipe.ArgumentsJson;
                row.Name = Truncate(entry.Name);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string? name) =>
        string.IsNullOrEmpty(name) ? string.Empty : name.Length <= NameMaxLength ? name : name[..NameMaxLength];
}
