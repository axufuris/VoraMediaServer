using Microsoft.Extensions.DependencyInjection;
using Vora.Application.Analysis;
using Vora.Application.Tasks;
using Vora.Application.Thumbnails;
using Vora.Plugins.Interfaces;

namespace Vora.Application.Tests.Tasks;

public class LibraryTaskLifecycleTests
{
    private readonly TaskQueueManager _queue = new(Substitute.For<IClientNotifier>());
    private readonly Guid _movies = Guid.NewGuid();

    private Guid IdOf(string name) => _queue.GetAllTasks().Single(t => t.Name == name).Id;

    [Fact]
    public void Thumbnails_are_their_own_task_under_their_own_name()
    {
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);

        _queue.GetAllTasks().Select(t => t.Name).Should().Equal("Generate Video Thumbnails: Movies");
    }

    [Fact]
    public void A_scans_thumbnail_request_merges_into_the_waiting_nightly_one_instead_of_being_dropped()
    {
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isScheduleTrigger: true);
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);

        _queue.GetAllTasks().Should().HaveCount(1);
        _queue.PendingThumbnailReasons(_movies).Should().Be(LibraryThumbnailReason.Schedule | LibraryThumbnailReason.Addition);
    }

    [Fact]
    public void A_thumbnail_request_while_a_pass_runs_gets_a_pass_after_it()
    {
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);
        var running = IdOf("Generate Video Thumbnails: Movies");
        _queue.MarkTaskAsRunning(running);

        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);
        _queue.RemoveTask(running);

        _queue.GetAllTasks().Select(t => t.Name).Should().Equal("Generate Video Thumbnails: Movies");
    }

    [Fact]
    public void Cancelling_a_queued_reanalyze_does_not_leave_its_force_for_the_next_analysis()
    {
        _queue.QueueAnalyzeLibraryMediaContent(_movies, "Movies", forceOverride: true);
        _queue.CancelTask(IdOf("Analyze Library: Movies"));

        _queue.QueueLibraryPostScan(_movies, "Movies");

        _queue.PendingAnalysisReasons(_movies).Should().Be(LibraryAnalysisReason.Addition);
    }

    [Fact]
    public void Cancelling_a_running_analysis_drops_the_reasons_its_follow_up_would_have_used()
    {
        _queue.QueueLibraryPostScan(_movies, "Movies");
        var running = IdOf("Analyze Library: Movies");
        _queue.MarkTaskAsRunning(running);
        _queue.TakeAnalysisReasons(_movies);
        _queue.QueueAnalyzeLibraryMediaContent(_movies, "Movies", forceOverride: true);

        _queue.CancelTask(running);
        _queue.RemoveTask(running);

        _queue.PendingAnalysisReasons(_movies).Should().Be(LibraryAnalysisReason.None);
        _queue.GetAllTasks().Should().BeEmpty();
    }

    [Fact]
    public void Cancelling_a_queued_regenerate_all_does_not_leave_its_force_behind()
    {
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", forceOverride: true);
        _queue.CancelTask(IdOf("Generate Video Thumbnails: Movies"));

        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);

        _queue.PendingThumbnailReasons(_movies).Should().Be(LibraryThumbnailReason.Addition);
    }

    [Fact]
    public void Deleting_a_library_cancels_its_analysis_thumbnails_and_subtitles_but_not_another_librarys()
    {
        var shows = Guid.NewGuid();
        _queue.QueueLibraryPostScan(_movies, "Movies");
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);
        _queue.QueuePreExtractLibrarySubtitles(_movies, "Movies");
        _queue.QueueScanLibrary(_movies, "Movies");
        _queue.QueueLibraryPostScan(shows, "Shows");

        _queue.QueueDeleteLibrary(_movies, "Movies");

        _queue.GetAllTasks().Select(t => t.Name).Should().BeEquivalentTo("Analyze Library: Shows", "Delete Library: Movies");
    }

    [Fact]
    public async Task The_delete_waits_for_a_running_library_task_to_stop()
    {
        _queue.QueueGenerateLibraryVideoThumbnails(_movies, "Movies", isAdditionTrigger: true);
        var running = IdOf("Generate Video Thumbnails: Movies");
        await Task.Run(() => _queue.MarkTaskAsRunning(running), TestContext.Current.CancellationToken);

        var wait = _queue.WaitForLibraryTasksToStopAsync(_movies, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(400, TestContext.Current.CancellationToken);

        wait.IsCompleted.Should().BeFalse();
        _queue.GetAllTasks().Single().Status.Should().Be("Cancelling");

        _queue.RemoveTask(running);
        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_delete_does_not_wait_on_other_libraries()
    {
        _queue.QueueLibraryPostScan(Guid.NewGuid(), "Shows");
        var shows = IdOf("Analyze Library: Shows");
        await Task.Run(() => _queue.MarkTaskAsRunning(shows), TestContext.Current.CancellationToken);

        await _queue.WaitForLibraryTasksToStopAsync(_movies, cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Saving_a_library_twice_queues_one_update()
    {
        _queue.QueueLibraryUpdated(_movies, "Movies");
        _queue.QueueLibraryUpdated(_movies, "Movies");

        _queue.GetAllTasks().Should().HaveCount(1);
    }

    [Fact]
    public void Changing_folders_while_the_library_scans_rescans_after()
    {
        _queue.QueueScanLibrary(_movies, "Movies");
        var running = IdOf("Scan Library: Movies");
        _queue.MarkTaskAsRunning(running);

        _queue.QueueLibraryUpdated(_movies, "Movies");
        _queue.RemoveTask(running);

        _queue.GetAllTasks().Select(t => t.Name).Should().Equal("Update Library: Movies");
    }

    [Fact]
    public void Repeated_forced_scans_queue_once()
    {
        _queue.QueueScanLibrary(_movies, "Movies", forceOverride: true);
        _queue.QueueScanLibrary(_movies, "Movies", forceOverride: true);

        _queue.GetAllTasks().Should().HaveCount(1);
    }

    private static (IServiceProvider Sp, IVideoThumbnailManager Thumbnails, ITaskQueueManager Queue) Services()
    {
        var thumbnails = Substitute.For<IVideoThumbnailManager>();
        var queue = Substitute.For<ITaskQueueManager>();
        var sp = new ServiceCollection()
            .AddSingleton(thumbnails)
            .AddSingleton(queue)
            .AddSingleton<ITaskProgressReporter>(new NullTaskProgressReporter())
            .BuildServiceProvider();
        return (sp, thumbnails, queue);
    }

    [Fact]
    public async Task A_nightly_and_a_scan_request_run_both_trigger_checks()
    {
        var (sp, thumbnails, _) = Services();
        var library = Guid.NewGuid();

        await TaskQueueManager.RunLibraryThumbnailsAsync(sp, library, LibraryThumbnailReason.Schedule | LibraryThumbnailReason.Addition, CancellationToken.None);

        await thumbnails.Received(1).TriggerLibraryThumbnailGenerationAsync(library, false, false, true, Arg.Any<CancellationToken>());
        await thumbnails.Received(1).TriggerLibraryThumbnailGenerationAsync(library, false, true, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Regenerate_missing_ignores_the_trigger_setting()
    {
        var (sp, thumbnails, _) = Services();
        var library = Guid.NewGuid();

        await TaskQueueManager.RunLibraryThumbnailsAsync(sp, library, LibraryThumbnailReason.Manual | LibraryThumbnailReason.Addition, CancellationToken.None);

        await thumbnails.Received(1).TriggerLibraryThumbnailGenerationAsync(library, false, false, false, Arg.Any<CancellationToken>());
        await thumbnails.ReceivedWithAnyArgs(1).TriggerLibraryThumbnailGenerationAsync(default);
    }

    [Fact]
    public async Task Regenerate_all_wins_over_everything_else()
    {
        var (sp, thumbnails, _) = Services();
        var library = Guid.NewGuid();

        await TaskQueueManager.RunLibraryThumbnailsAsync(sp, library, LibraryThumbnailReason.Force | LibraryThumbnailReason.Manual | LibraryThumbnailReason.Schedule, CancellationToken.None);

        await thumbnails.Received(1).TriggerLibraryThumbnailGenerationAsync(library, true, false, false, Arg.Any<CancellationToken>());
        await thumbnails.ReceivedWithAnyArgs(1).TriggerLibraryThumbnailGenerationAsync(default);
    }

    [Fact]
    public async Task New_items_queue_a_thumbnail_pass_when_the_library_wants_them_on_addition()
    {
        var (sp, thumbnails, queue) = Services();
        var library = Guid.NewGuid();
        thumbnails.WantsAdditionThumbnailsAsync(library).Returns(true);

        await TaskQueueManager.QueueAdditionThumbnailsIfWantedAsync(sp, library, "Movies");

        queue.Received(1).QueueGenerateLibraryVideoThumbnails(library, "Movies", false, false, true);
    }

    [Fact]
    public async Task No_thumbnail_task_shows_up_when_the_library_does_not_want_them_on_addition()
    {
        var (sp, thumbnails, queue) = Services();
        var library = Guid.NewGuid();
        thumbnails.WantsAdditionThumbnailsAsync(library).Returns(false);

        await TaskQueueManager.QueueAdditionThumbnailsIfWantedAsync(sp, library, "Movies");

        queue.DidNotReceiveWithAnyArgs().QueueGenerateLibraryVideoThumbnails(default);
    }

    [Fact]
    public async Task The_delete_stops_a_running_task_on_one_of_the_librarys_items()
    {
        var episode = Guid.NewGuid();
        _queue.QueueGenerateMediaItemVideoThumbnails(episode, "Pilot", forceOverride: true);
        var running = IdOf("Generate Video Thumbnails: Pilot");
        await Task.Run(() => _queue.MarkTaskAsRunning(running), TestContext.Current.CancellationToken);

        var wait = _queue.WaitForLibraryTasksToStopAsync(_movies, new HashSet<Guid> { episode }, TestContext.Current.CancellationToken);
        await Task.Delay(400, TestContext.Current.CancellationToken);

        wait.IsCompleted.Should().BeFalse();
        _queue.GetAllTasks().Single().Status.Should().Be("Cancelling");

        _queue.RemoveTask(running);
        await wait.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_delete_leaves_other_libraries_items_alone()
    {
        _queue.QueueGenerateMediaItemVideoThumbnails(Guid.NewGuid(), "Elsewhere", forceOverride: true);

        await _queue.WaitForLibraryTasksToStopAsync(_movies, new HashSet<Guid> { Guid.NewGuid() }, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        _queue.GetAllTasks().Single().Status.Should().Be("Pending");
    }
}
