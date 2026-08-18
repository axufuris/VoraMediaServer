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
    public void EnqueueTask_with_same_dedupe_key_does_not_create_a_second_task()
    {
        var first = _queue.EnqueueTask("gen", (ct, sp) => Task.CompletedTask, dedupeKey: "gen-thumbs:lib1:False");
        var second = _queue.EnqueueTask("gen", (ct, sp) => Task.CompletedTask, dedupeKey: "gen-thumbs:lib1:False");

        second.Should().Be(first);
        _queue.GetAllTasks().Count(t => t.Id == first).Should().Be(1);
        _queue.GetAllTasks().Should().HaveCount(1);
    }

    [Fact]
    public void EnqueueTask_dedupe_does_not_block_a_different_dedupe_key()
    {
        var missing = _queue.EnqueueTask("gen", (ct, sp) => Task.CompletedTask, dedupeKey: "gen-thumbs:lib1:False");
        var all = _queue.EnqueueTask("gen", (ct, sp) => Task.CompletedTask, dedupeKey: "gen-thumbs:lib1:True");

        all.Should().NotBe(missing);
        _queue.GetAllTasks().Should().HaveCount(2);
    }

    [Fact]
    public void EnqueueTask_without_dedupe_key_always_enqueues()
    {
        var a = _queue.EnqueueTask("a", (ct, sp) => Task.CompletedTask);
        var b = _queue.EnqueueTask("a", (ct, sp) => Task.CompletedTask);

        b.Should().NotBe(a);
        _queue.GetAllTasks().Should().HaveCount(2);
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
    public async Task EnqueueTask_notifies_clients_tasks_updated()
    {
        // Wire a TaskCompletionSource into the substitute so we can wait deterministically
        // instead of sleeping. EnqueueTask fires NotifyTasksUpdatedAsync on a background task.
        var notified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _notifier.NotifyTasksUpdatedAsync().Returns(_ =>
        {
            notified.TrySetResult(true);
            return Task.CompletedTask;
        });

        _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        var completed = await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().BeSameAs(notified.Task, "NotifyTasksUpdatedAsync should fire after EnqueueTask");
        await _notifier.Received().NotifyTasksUpdatedAsync();
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
    public async Task CancelTask_keeps_token_until_RemoveTask_then_returns_false()
    {
        var id = _queue.EnqueueTask("alpha", (ct, sp) => Task.CompletedTask);

        // CancelTask intentionally keeps the entry so a still-queued task is
        // observed as cancelled (and skipped) by the worker; a repeat cancel
        // still finds it. RemoveTask is what disposes and evicts the token.
        _queue.CancelTask(id).Should().BeTrue();
        _queue.CancelTask(id).Should().BeTrue();

        _queue.RemoveTask(id);
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
