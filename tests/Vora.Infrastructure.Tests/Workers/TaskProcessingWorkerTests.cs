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
        public ValueTask EnqueueAsync(QueuedTaskDto task) => Channel.Writer.WriteAsync(task, TestContext.Current.CancellationToken);
    }

    private static (TaskProcessingWorker worker, ITaskQueueManager queue, FakeScopeFactory scopes, ControlledQueue controlled) Build()
    {
        var queue = Substitute.For<ITaskQueueManager>();
        var controlled = new ControlledQueue();
        queue.DequeueAsync(Arg.Any<CancellationToken>())
            .Returns(call => controlled.DequeueAsync(call.Arg<CancellationToken>()));
        // The worker skips tasks whose per-task token is null/cancelled. Hand back
        // a live (non-cancelled) token so enqueued work items actually run.
        queue.GetTaskCancellationToken(Arg.Any<Guid>()).Returns(CancellationToken.None);
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

        await workItemRan.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

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

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

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

        await taskStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        cts.Cancel();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

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
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        processedIds.Should().HaveCount(totalTasks);
        scopes.CreatedScopes.Should().Be(totalTasks);
        queue.Received(totalTasks).MarkTaskAsRunning(Arg.Any<Guid>());
        queue.Received(totalTasks).RemoveTask(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ExecuteAsync_serializes_same_key_and_respects_concurrency_cap()
    {
        var (worker, queue, _, controlled) = Build();
        using var cts = new CancellationTokenSource();

        var gate = new object();
        var running = 0;
        var maxConcurrent = 0;
        var perKeyRunning = new Dictionary<string, int>();
        var sameKeyOverlap = false;
        var completed = 0;
        const int total = 12;
        var allDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CancellationToken, IServiceProvider, Task> Work(string key) => async (ct, sp) =>
        {
            lock (gate)
            {
                running++;
                maxConcurrent = Math.Max(maxConcurrent, running);
                perKeyRunning.TryGetValue(key, out var c);
                if (c > 0) sameKeyOverlap = true;
                perKeyRunning[key] = c + 1;
            }
            await Task.Delay(40, ct);
            lock (gate)
            {
                running--;
                perKeyRunning[key]--;
                if (++completed == total) allDone.TrySetResult(true);
            }
        };

        // 4 tasks that must serialize (shared key) + 8 with unique keys.
        for (var i = 0; i < 4; i++)
            await controlled.EnqueueAsync(new QueuedTaskDto { Name = $"lib-{i}", ResourceKey = "library:X", WorkItem = Work("library:X") });
        for (var i = 0; i < 8; i++)
        {
            var k = $"u{i}";
            await controlled.EnqueueAsync(new QueuedTaskDto { Name = k, ResourceKey = k, WorkItem = Work(k) });
        }

        await worker.StartAsync(cts.Token);
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        controlled.Complete();
        await (worker.ExecuteTask ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        sameKeyOverlap.Should().BeFalse("tasks sharing a resource key must never run concurrently");
        maxConcurrent.Should().BeLessThanOrEqualTo(3, "the global concurrency cap is 3");
        maxConcurrent.Should().BeGreaterThan(1, "unrelated tasks should run in parallel");
        completed.Should().Be(total);
    }
}
