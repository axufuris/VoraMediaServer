using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Tasks;
using Vora.Application.Tasks.Dtos;

namespace Vora.Infrastructure.Workers;

public class TaskProcessingWorker : BackgroundService
{
    // Up to this many tasks run at once. Kept low because heavy jobs already
    // parallelize internally (scan/analysis/overlays run several items at once),
    // so a high cap would over-subscribe CPU and providers.
    private const int MaxConcurrency = 3;

    private readonly ITaskQueueManager _taskQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskProcessingWorker> _logger;

    public TaskProcessingWorker(
        ITaskQueueManager taskQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<TaskProcessingWorker> logger)
    {
        _taskQueue = taskQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Task Processing Worker is starting.");

        var gate = new object();
        var pending = new List<QueuedTaskDto>();
        var runningKeys = new HashSet<string>(StringComparer.Ordinal);
        var inFlight = new List<Task>();
        var runningCount = 0;
        var producerDone = false;
        // Not disposed: this outlives some running-task continuations that signal
        // it in their finally block; a `using` would risk an ObjectDisposedException.
        var wakeup = new SemaphoreSlim(0);

        void Signal() { try { wakeup.Release(); } catch (ObjectDisposedException) { } }

        // Producer: drain the queue into the pending list and nudge the dispatcher.
        // In production the queue never completes; this loop matters for shutdown
        // and for draining a finite queue (tests).
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var task in _taskQueue.DequeueAsync(stoppingToken))
                {
                    lock (gate) pending.Add(task);
                    Signal();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (gate) producerDone = true;
                Signal();
            }
        }, stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<QueuedTaskDto> toStart = new();
                bool drained;
                lock (gate)
                {
                    inFlight.RemoveAll(t => t.IsCompleted);

                    // Start the earliest pending tasks whose resource key isn't
                    // already in flight, up to the concurrency cap. A task whose
                    // key is busy stays pending (preserving FIFO within a key)
                    // and is retried when that key frees.
                    for (int i = 0; i < pending.Count && runningCount + toStart.Count < MaxConcurrency; i++)
                    {
                        var task = pending[i];
                        if (runningKeys.Contains(task.ResourceKey)) continue;
                        runningKeys.Add(task.ResourceKey);
                        toStart.Add(task);
                    }
                    foreach (var t in toStart) pending.Remove(t);
                    runningCount += toStart.Count;
                    drained = producerDone && pending.Count == 0 && runningCount == 0;
                }

                foreach (var task in toStart)
                {
                    var run = Task.Run(async () =>
                    {
                        try
                        {
                            await RunTaskAsync(task, stoppingToken);
                        }
                        finally
                        {
                            lock (gate)
                            {
                                runningKeys.Remove(task.ResourceKey);
                                runningCount--;
                            }
                            Signal();
                        }
                    }, stoppingToken);
                    lock (gate) inFlight.Add(run);
                }

                if (drained) break;

                await wakeup.WaitAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Task Processing Worker is stopping cleanly.");
        }
        finally
        {
            // Let in-flight tasks (now cancelled via the linked token) finish so
            // each removes itself from the task list before the worker exits.
            Task[] remaining;
            lock (gate) remaining = inFlight.ToArray();
            try { await Task.WhenAll(remaining); } catch { /* per-task errors already logged */ }
        }
    }

    private async Task RunTaskAsync(QueuedTaskDto task, CancellationToken stoppingToken)
    {
        // A task cancelled while still queued has no live token (or a
        // pre-cancelled one) — skip it instead of running it.
        var taskToken = _taskQueue.GetTaskCancellationToken(task.Id);
        if (taskToken == null || taskToken.Value.IsCancellationRequested)
        {
            _logger.LogInformation("Skipping cancelled task: {TaskName} ({TaskId})", task.Name, task.Id);
            _taskQueue.RemoveTask(task.Id);
            return;
        }

        // Link the app-lifetime token with the per-task token so
        // CancelTask(taskId) actually reaches the running work item.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, taskToken.Value);

        try
        {
            using var scope = _scopeFactory.CreateScope();

            // Resolve a friendly display name (e.g. library/media title) before
            // marking running, so the UI never shows a raw GUID.
            var resolver = _taskQueue.GetTaskNameResolver(task.Id);
            if (resolver != null)
            {
                try
                {
                    var resolved = await resolver(scope.ServiceProvider);
                    if (!string.IsNullOrWhiteSpace(resolved)) _taskQueue.UpdateTaskName(task.Id, resolved);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Task name resolution failed for {TaskId}", task.Id);
                }
            }

            _logger.LogInformation("Starting Task: {TaskName}", task.Name);
            _taskQueue.MarkTaskAsRunning(task.Id);

            await task.WorkItem(linkedCts.Token, scope.ServiceProvider);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Task Cancelled: {TaskName} ({TaskId})", task.Name, task.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Task Failed: {TaskName} ({TaskId})", task.Name, task.Id);
        }
        finally
        {
            _taskQueue.RemoveTask(task.Id);
        }
    }
}
