using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class TaskQueuePagingTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>());

    private void Enqueue(int count, string prefix = "Scan File")
    {
        for (var i = 0; i < count; i++)
        {
            _queue.EnqueueTask($"{prefix} {i:D4}", (ct, sp) => Task.CompletedTask);
        }
    }

    [Fact]
    public void A_page_returns_one_window_and_the_full_total()
    {
        Enqueue(120);

        var page = _queue.GetTaskPage(skip: 0, take: 25);

        page.Items.Should().HaveCount(25);
        page.Total.Should().Be(120);
        page.Skip.Should().Be(0);
        page.Take.Should().Be(25);
    }

    [Fact]
    public void Pages_walk_the_queue_without_repeating_a_task()
    {
        Enqueue(60);

        var first = _queue.GetTaskPage(0, 25).Items.Select(t => t.Id);
        var second = _queue.GetTaskPage(25, 25).Items.Select(t => t.Id);
        var last = _queue.GetTaskPage(50, 25).Items;

        last.Should().HaveCount(10);
        first.Concat(second).Concat(last.Select(t => t.Id)).Should().OnlyHaveUniqueItems().And.HaveCount(60);
    }

    [Fact]
    public async Task The_task_actually_running_is_on_the_first_page()
    {
        Enqueue(80);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Guid runningId = Guid.Empty;
        await foreach (var task in _queue.DequeueAsync(cts.Token))
        {
            runningId = task.Id;
            _queue.MarkTaskAsRunning(task.Id);
            break;
        }

        var page = _queue.GetTaskPage(0, 25);

        page.Items.First().Id.Should().Be(runningId);
        page.Items.First().Status.Should().Be("Running");
        page.Running.Should().Be(1);
    }

    [Fact]
    public void An_oversized_page_is_capped()
    {
        Enqueue(500);

        _queue.GetTaskPage(0, 5000).Items.Should().HaveCount(200);
    }

    [Fact]
    public void A_nonsense_window_is_brought_back_into_range()
    {
        Enqueue(10);

        _queue.GetTaskPage(-5, 25).Skip.Should().Be(0);
        _queue.GetTaskPage(0, 0).Take.Should().Be(1);
    }

    [Fact]
    public void Paging_past_the_end_returns_nothing_rather_than_throwing()
    {
        Enqueue(10);

        var page = _queue.GetTaskPage(500, 25);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(10);
    }

    [Fact]
    public void An_empty_queue_reports_nothing_to_show()
    {
        var page = _queue.GetTaskPage(0, 25);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
        page.Running.Should().Be(0);
    }
}
