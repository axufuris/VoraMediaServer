using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Tasks;

namespace Vora.Infrastructure.Workers;

public class TaskProcessingWorker : BackgroundService
{
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

        try
        {
            await foreach (var task in _taskQueue.DequeueAsync(stoppingToken))
            {
                // A task cancelled while still queued has no live token (or a
                // pre-cancelled one) — skip it instead of running it.
                var taskToken = _taskQueue.GetTaskCancellationToken(task.Id);
                if (taskToken == null || taskToken.Value.IsCancellationRequested)
                {
                    _logger.LogInformation("Skipping cancelled task: {TaskName} ({TaskId})", task.Name, task.Id);
                    _taskQueue.RemoveTask(task.Id);
                    continue;
                }

                // Link the app-lifetime token with the per-task token so
                // CancelTask(taskId) actually reaches the running work item.
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, taskToken.Value);

                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    // Resolve a friendly display name (e.g. library/media title)
                    // before marking running, so the UI never shows a raw GUID.
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
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Task Processing Worker is stopping cleanly.");
        }
    }
}
