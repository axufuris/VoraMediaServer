using Vora.Application.Analysis;
using Vora.Application.Tasks;

namespace Vora.Application.Tests.Tasks;

public class SubtitleTaskMergeTests
{
    private static readonly Guid Shows = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Movies = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>(), Substitute.For<ITaskJournal>());

    private static Guid Item(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:D12}");

    private List<string> Names() => _queue.GetAllTasks().Select(t => t.Name).ToList();

    [Fact]
    public void An_item_queues_on_its_own_when_no_library_pass_is_active()
    {
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Heat", Movies);

        Names().Should().Equal("Pre-extract Subtitles: Heat");
    }

    [Fact]
    public void An_item_joins_a_library_pass_that_has_not_started()
    {
        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");

        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot", Shows);

        Names().Should().Equal("Pre-extract Subtitles: Shows");
    }

    [Fact]
    public void Items_that_arrive_during_a_library_pass_run_it_again_afterwards()
    {
        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");
        var running = _queue.GetAllTasks().Single().Id;
        _queue.MarkTaskAsRunning(running);

        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot", Shows);
        _queue.QueuePreExtractMediaItemSubtitles(Item(2), "Nosedive", Shows);

        _queue.GetAllTasks().Should().ContainSingle().Which.Id.Should().Be(running);

        _queue.RemoveTask(running);

        var rerun = _queue.GetAllTasks().Should().ContainSingle().Subject;
        rerun.Name.Should().Be("Pre-extract Subtitles: Shows");
        rerun.Status.Should().Be("Pending");
    }

    [Fact]
    public void A_cancelled_library_pass_is_not_run_again()
    {
        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");
        var running = _queue.GetAllTasks().Single().Id;
        _queue.MarkTaskAsRunning(running);
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot", Shows);

        _queue.CancelTask(running);
        _queue.RemoveTask(running);

        _queue.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void Items_from_another_library_wait_as_one_pass_for_that_library()
    {
        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");
        _queue.MarkTaskAsRunning(_queue.GetAllTasks().Single().Id);

        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Heat", Movies);
        _queue.QueuePreExtractMediaItemSubtitles(Item(2), "Alien", Movies);

        _queue.GetAllTasks().Should().HaveCount(2);
        _queue.GetAllTasks().Should().ContainSingle(t => t.Status == "Pending")
            .Which.Name.Should().Be($"Pre-extract Subtitles: {Movies}");
    }

    [Fact]
    public void A_library_pass_takes_over_the_items_already_waiting_in_that_library()
    {
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot", Shows);
        _queue.QueuePreExtractMediaItemSubtitles(Item(2), "Nosedive", Shows);
        _queue.QueuePreExtractMediaItemSubtitles(Item(3), "Heat", Movies);

        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");

        Names().Should().BeEquivalentTo("Pre-extract Subtitles: Heat", "Pre-extract Subtitles: Shows");
    }

    [Fact]
    public void An_item_already_running_is_left_to_finish()
    {
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot", Shows);
        _queue.MarkTaskAsRunning(_queue.GetAllTasks().Single().Id);

        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");

        _queue.GetAllTasks().Should().HaveCount(2);
    }

    [Fact]
    public void A_backfill_that_has_not_started_covers_waiting_and_new_items()
    {
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Heat", Movies);

        _queue.QueueSubtitleBackfill();
        _queue.QueuePreExtractMediaItemSubtitles(Item(2), "Pilot", Shows);

        Names().Should().Equal("Pre-extract Subtitles: whole library backfill");
    }

    [Fact]
    public void An_item_whose_library_is_unknown_still_queues_on_its_own()
    {
        _queue.QueuePreExtractLibrarySubtitles(Shows, "Shows");

        _queue.QueuePreExtractMediaItemSubtitles(Item(1), "Pilot");

        _queue.GetAllTasks().Should().HaveCount(2);
    }

    [Fact]
    public void Tasks_named_together_are_renamed_and_not_looked_up_again()
    {
        _queue.QueuePreExtractMediaItemSubtitles(Item(1), null, Movies);
        _queue.QueuePreExtractMediaItemSubtitles(Item(2), null, Movies);
        var ids = _queue.GetAllTasks().Select(t => t.Id).ToList();
        _queue.GetTaskNameResolver(ids[0]).Should().NotBeNull();

        _queue.UpdateTaskNames(new Dictionary<Guid, string>
        {
            [ids[0]] = "Pre-extract Subtitles: Heat",
            [ids[1]] = "Pre-extract Subtitles: Alien",
        });

        Names().Should().BeEquivalentTo("Pre-extract Subtitles: Heat", "Pre-extract Subtitles: Alien");
        _queue.GetTaskNameResolver(ids[0]).Should().BeNull();
        _queue.GetTaskNameResolver(ids[1]).Should().BeNull();
    }
}
