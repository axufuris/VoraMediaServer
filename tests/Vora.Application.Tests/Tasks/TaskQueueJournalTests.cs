using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class TaskQueueJournalTests
{
    private static readonly Guid Library = Guid.NewGuid();

    private readonly ITaskJournal _journal = Substitute.For<ITaskJournal>();
    private readonly TaskQueueManager _queue;

    public TaskQueueJournalTests()
    {
        _queue = new TaskQueueManager(Substitute.For<IClientNotifier>(), _journal);
    }

    private Guid OnlyTaskId() => _queue.GetAllTasks().Single().Id;

    [Fact]
    public void A_queued_task_is_saved_with_its_recipe()
    {
        _queue.QueueScanLibrary(Library, "Movies");

        _journal.Received(1).Record(OnlyTaskId(), "Scan Library: Movies",
            Arg.Is<TaskRecipe>(r => r.Kind == nameof(ITaskQueueManager.QueueScanLibrary) && r.Guid("libraryId") == Library));
    }

    [Fact]
    public void A_task_that_finishes_is_removed_from_the_saved_queue()
    {
        _queue.QueueScanLibrary(Library, "Movies");
        var id = OnlyTaskId();
        _queue.MarkTaskAsRunning(id);

        _queue.RemoveTask(id);

        _journal.Received(1).Complete(id);
    }

    [Fact]
    public void A_task_the_server_stopped_stays_saved()
    {
        _queue.QueueScanLibrary(Library, "Movies");
        var id = OnlyTaskId();
        _queue.MarkTaskAsRunning(id);

        _queue.RemoveTask(id, interrupted: true);

        _journal.DidNotReceive().Complete(Arg.Any<Guid>());
        _queue.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void A_task_cancelled_before_it_ran_is_removed_from_the_saved_queue()
    {
        _queue.QueueScanLibrary(Library, "Movies");
        var id = OnlyTaskId();

        _queue.CancelTask(id);

        _journal.Received(1).Complete(id);
    }

    [Fact]
    public void A_second_request_that_joins_a_queued_analysis_saves_the_combined_reasons()
    {
        _queue.QueueLibraryAnalysis(Library, "Shows", LibraryAnalysisReason.Addition);
        var id = OnlyTaskId();

        _queue.QueueLibraryAnalysis(Library, "Shows", LibraryAnalysisReason.Force);

        _queue.GetAllTasks().Should().ContainSingle();
        _journal.Received().Record(id, Arg.Any<string>(),
            Arg.Is<TaskRecipe>(r => r.Number("reason") == (int)(LibraryAnalysisReason.Addition | LibraryAnalysisReason.Force)));
    }

    [Fact]
    public void A_follow_up_run_is_saved_when_it_is_queued()
    {
        _queue.QueueLibraryUpdated(Library, "Movies");
        var first = OnlyTaskId();
        _queue.MarkTaskAsRunning(first);
        _queue.QueueLibraryUpdated(Library, "Movies");

        _queue.RemoveTask(first);

        var followUp = OnlyTaskId();
        followUp.Should().NotBe(first);
        _journal.Received(1).Complete(first);
        _journal.Received(1).Record(followUp, Arg.Any<string>(), Arg.Is<TaskRecipe>(r => r.Kind == nameof(ITaskQueueManager.QueueLibraryUpdated)));
    }

    [Fact]
    public void A_one_off_task_queued_with_its_own_code_is_not_saved()
    {
        var id = _queue.EnqueueTask("Refresh Music Recommendations", (ct, sp) => Task.CompletedTask);

        _queue.RemoveTask(id);

        _journal.DidNotReceive().Record(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<TaskRecipe>());
        _journal.DidNotReceive().Complete(Arg.Any<Guid>());
    }
}
