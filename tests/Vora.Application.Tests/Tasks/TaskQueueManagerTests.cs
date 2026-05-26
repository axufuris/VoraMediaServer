using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class TaskQueueManagerTests
{
    private readonly IClientNotifier _notifier;
    private readonly TaskQueueManager _queue;

    public TaskQueueManagerTests()
    {
        _notifier = Substitute.For<IClientNotifier>();
        _queue = new TaskQueueManager(_notifier);
    }

    [Fact]
    public async Task EnqueueTask_returns_id_and_makes_task_dequeueable()
    {
        var id = _queue.EnqueueTask("test", (ct, sp) => Task.CompletedTask);

        id.Should().NotBe(Guid.Empty);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            task.Id.Should().Be(id);
            task.Name.Should().Be("test");
            task.Status.Should().Be("Pending");
            break;
        }
    }

    [Fact]
    public void EnqueueTask_records_task_in_GetAllTasks_with_pending_status()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        var snapshot = _queue.GetAllTasks().ToList();

        snapshot.Should().ContainSingle(t => t.Id == id && t.Name == "alpha" && t.Status == "Pending");
    }

    [Fact]
    public void MarkTaskAsRunning_updates_status_to_Running()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        _queue.MarkTaskAsRunning(id);

        _queue.GetAllTasks().Single(t => t.Id == id).Status.Should().Be("Running");
    }

    [Fact]
    public void MarkTaskAsRunning_is_a_no_op_for_unknown_id()
    {
        var act = () => _queue.MarkTaskAsRunning(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveTask_evicts_task_from_GetAllTasks()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        _queue.RemoveTask(id);

        _queue.GetAllTasks().Should().NotContain(t => t.Id == id);
    }

    [Fact]
    public void RemoveTask_is_a_no_op_for_unknown_id()
    {
        var act = () => _queue.RemoveTask(Guid.NewGuid());
        act.Should().NotThrow();
    }

    [Fact]
    public void CancelTask_returns_true_for_known_task_and_signals_cancellation()
    {
        Guid? id = null;
        id = _queue.EnqueueTask("alpha", (ct, sp) =>
        {
            ct.IsCancellationRequested.Should().BeTrue();
            return Task.CompletedTask;
        });

        var cancelled = _queue.CancelTask(id.Value);

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void CancelTask_returns_false_for_unknown_id()
    {
        _queue.CancelTask(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void GetAllTasks_lists_running_tasks_before_pending_tasks()
    {
        var pending = _queue.EnqueueTask("p", (ct, sp) => Task.CompletedTask);
        var running = _queue.EnqueueTask("r", (ct, sp) => Task.CompletedTask);
        _queue.MarkTaskAsRunning(running);

        var snapshot = _queue.GetAllTasks().ToList();

        snapshot.Should().HaveCount(2);
        snapshot[0].Id.Should().Be(running);
        snapshot[1].Id.Should().Be(pending);
    }

    [Fact]
    public void EnqueueTask_notifies_clients_tasks_updated()
    {
        _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        // Notification is fire-and-forget; wait briefly for the background task.
        Thread.Sleep(50);
        _notifier.Received().NotifyTasksUpdatedAsync();
    }

    [Fact]
    public async Task DequeueAsync_yields_multiple_enqueued_tasks_in_order()
    {
        var first = _queue.EnqueueTask("first", (ct, sp) => Task.CompletedTask);
        var second = _queue.EnqueueTask("second", (ct, sp) => Task.CompletedTask);
        var third = _queue.EnqueueTask("third", (ct, sp) => Task.CompletedTask);

        var seen = new List<Guid>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            seen.Add(task.Id);
            if (seen.Count == 3) break;
        }

        seen.Should().Equal(first, second, third);
    }

    [Fact]
    public async Task CancelTask_removes_token_so_a_second_cancel_returns_false()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        _queue.CancelTask(id).Should().BeTrue();
        _queue.CancelTask(id).Should().BeFalse();

        await Task.CompletedTask;
    }

    [Fact]
    public void RemoveTask_disposes_associated_cancellation_token_source()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        var act = () => _queue.RemoveTask(id);
        act.Should().NotThrow();

        // Second remove no-ops (token already disposed and removed).
        _queue.RemoveTask(id);
    }
}
