using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

// Every ingestion task for a library shares its resource key, and the dispatcher
// is FIFO within a key — so a queued delete sat BEHIND all of them. Deleting a
// library mid-scan meant watching hundreds of scans of the thing being deleted
// run to completion first.
public class LibraryTaskCancellationTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>());

    private static string LibraryKey(Guid libraryId) => $"library:{libraryId}";

    [Fact]
    public void Cancelling_a_librarys_tasks_clears_its_pending_work()
    {
        var libraryId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            _queue.EnqueueTask($"Scan File: {i}", (ct, sp) => Task.CompletedTask, resourceKey: LibraryKey(libraryId));
        }

        var cancelled = _queue.CancelTasksForLibrary(libraryId);

        cancelled.Should().Be(5);
        _queue.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void Cancelling_one_librarys_tasks_leaves_another_librarys_alone()
    {
        var doomed = Guid.NewGuid();
        var other = Guid.NewGuid();

        _queue.EnqueueTask("Scan File: doomed", (ct, sp) => Task.CompletedTask, resourceKey: LibraryKey(doomed));
        var keep = _queue.EnqueueTask("Scan File: other", (ct, sp) => Task.CompletedTask, resourceKey: LibraryKey(other));

        _queue.CancelTasksForLibrary(doomed).Should().Be(1);

        _queue.GetAllTasks().Select(t => t.Id).Should().BeEquivalentTo(new[] { keep });
    }

    // Unkeyed tasks get a unique resource key, so a library sweep must not touch
    // unrelated work that happens to be queued at the same time.
    [Fact]
    public void Cancelling_a_librarys_tasks_leaves_unkeyed_tasks_alone()
    {
        var libraryId = Guid.NewGuid();

        _queue.EnqueueTask("Scan File", (ct, sp) => Task.CompletedTask, resourceKey: LibraryKey(libraryId));
        var unrelated = _queue.EnqueueTask("EPG Sync", (ct, sp) => Task.CompletedTask);

        _queue.CancelTasksForLibrary(libraryId);

        _queue.GetAllTasks().Select(t => t.Id).Should().BeEquivalentTo(new[] { unrelated });
    }

    [Fact]
    public void Cancelling_a_library_with_no_tasks_is_a_no_op()
    {
        _queue.CancelTasksForLibrary(Guid.NewGuid()).Should().Be(0);
    }

    // The delete must survive its own sweep: cancel first, enqueue second.
    [Fact]
    public void Queueing_a_delete_clears_the_scans_but_keeps_the_delete()
    {
        var libraryId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            _queue.EnqueueTask($"Scan File: {i}", (ct, sp) => Task.CompletedTask, resourceKey: LibraryKey(libraryId));
        }

        _queue.QueueDeleteLibrary(libraryId, "Music");

        var remaining = _queue.GetAllTasks().ToList();
        remaining.Should().ContainSingle();
        remaining[0].Name.Should().Be("Delete Library: Music");
    }
}
