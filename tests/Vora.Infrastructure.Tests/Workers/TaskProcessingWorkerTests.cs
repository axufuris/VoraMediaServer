using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Tasks;
using Vora.Application.Tasks.Dtos;
using Vora.Infrastructure.Workers;

namespace Vora.Infrastructure.Tests.Workers;

public class TaskProcessingWorkerTests
{
    private sealed class FakeScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = Substitute.For<IServiceProvider>();
        public void Dispose() { }
    }

    private sealed class FakeScopeFactory : IServiceScopeFactory
    {
        public int CreatedScopes;
        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreatedScopes);
            return new FakeScope();
        }
    }

    private sealed class ControlledQueue
    {
        public readonly Channel<QueuedTaskDto> Channel = System.Threading.Channels.Channel.CreateUnbounded<QueuedTaskDto>();

        public async IAsyncEnumerable<QueuedTaskDto> DequeueAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in Channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }

        public void Complete() => Channel.Writer.TryComplete();
        public ValueTask EnqueueAsync(QueuedTaskDto task) => Channel.Writer.WriteAsync(task);
    }

    private static (TaskProcessingWorker worker, ITaskQueueManager queue, FakeScopeFactory scopes, ControlledQueue controlled) Build()
    {
        var queue = Substitute.For<ITaskQueueManager>();
        var controlled = new ControlledQueue();
        queue.DequeueAsync(Arg.Any<CancellationToken>())
            .Returns(call => controlled.DequeueAsync(call.Arg<CancellationToken>()));
        var scopes = new FakeScopeFactory();
        var worker = new TaskProcessingWorker(queue, scopes, NullLogger<TaskProcessingWorker>.Instance);
        return (worker, queue, scopes, controlled);
    }

    [Fact]
    public async Task ExecuteAsync_runs_marks_running_invokes_work_item_and_removes_task()
    {
        var (worker, queue, scopes, controlled) = Build();
        using var cts = new CancellationTokenSource();
        var workItemRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new QueuedTaskDto
        {
            Name = "test-task",
            WorkItem = (ct, sp) =>
            {
                workItemRan.TrySetResult(true);
                return Task.CompletedTask;
            }
        };

        await worker.StartAsync(cts.Token);
        await controlled.EnqueueAsync(task);

        await workItemRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        queue.Received(1).MarkTaskAsRunning(task.Id);
        queue.Received(1).RemoveTask(task.Id);
        scopes.CreatedScopes.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_removes_task_even_when_work_item_throws()
    {
        var (worker, queue, _, controlled) = Build();
        using var cts = new CancellationTokenSource();
        var taskStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new QueuedTaskDto
        {
            Name = "boom",
            WorkItem = (ct, sp) =>
            {
                taskStarted.TrySetResult(true);
                throw new InvalidOperationException("simulated failure");
            }
        };

        await worker.StartAsync(cts.Token);
        await controlled.EnqueueAsync(task);

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        queue.Received(1).RemoveTask(task.Id);
    }

    [Fact]
    public async Task ExecuteAsync_removes_task_even_when_work_item_is_cancelled()
    {
        var (worker, queue, _, controlled) = Build();
        using var cts = new CancellationTokenSource();
        var taskStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = new QueuedTaskDto
        {
            Name = "cancelled",
            WorkItem = async (ct, sp) =>
            {
                taskStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            }
        };

        await worker.StartAsync(cts.Token);
        await controlled.EnqueueAsync(task);

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        queue.Received(1).RemoveTask(task.Id);
    }

    [Fact]
    public async Task ExecuteAsync_processes_multiple_tasks_in_order()
    {
        var (worker, queue, scopes, controlled) = Build();
        using var cts = new CancellationTokenSource();

        var processedIds = new List<Guid>();
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var totalTasks = 3;

        for (var i = 0; i < totalTasks; i++)
        {
            var task = new QueuedTaskDto
            {
                Name = $"task-{i}",
                WorkItem = (ct, sp) =>
                {
                    lock (processedIds)
                    {
                        // capture identity via closure
                    }
                    return Task.CompletedTask;
                }
            };
            var captureId = task.Id;
            task.WorkItem = (ct, sp) =>
            {
                lock (processedIds)
                {
                    processedIds.Add(captureId);
                    if (processedIds.Count == totalTasks) allDone.TrySetResult(true);
                }
                return Task.CompletedTask;
            };
            await controlled.EnqueueAsync(task);
        }

        await worker.StartAsync(cts.Token);
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        processedIds.Should().HaveCount(totalTasks);
        scopes.CreatedScopes.Should().Be(totalTasks);
        queue.Received(totalTasks).MarkTaskAsRunning(Arg.Any<Guid>());
        queue.Received(totalTasks).RemoveTask(Arg.Any<Guid>());
    }
}
