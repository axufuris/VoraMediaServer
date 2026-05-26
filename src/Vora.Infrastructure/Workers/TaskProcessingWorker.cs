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
                _logger.LogInformation("Starting Task: {TaskName}", task.Name);

                _taskQueue.MarkTaskAsRunning(task.Id);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await task.WorkItem(stoppingToken, scope.ServiceProvider);
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
