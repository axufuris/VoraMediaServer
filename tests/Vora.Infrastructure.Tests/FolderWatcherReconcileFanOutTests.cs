using Vora.Infrastructure.FileSystem;

namespace Vora.Infrastructure.Tests;

// Reconciliation catches the few files the watcher missed; it is not a scanner.
// A freshly added library has EVERY file un-ingested, so fanning out per file
// queued hundreds of tasks that duplicated the full scan the add had already
// queued — and each music task re-reads the whole ingested-path set, making the
// fan-out quadratic.
public class FolderWatcherReconcileFanOutTests
{
    [Fact]
    public void A_library_with_nothing_ingested_gets_one_library_scan()
    {
        FolderWatcherService.ShouldQueueFullScan(ingestedCount: 0, uningestedCount: 1).Should().BeTrue();
    }

    [Fact]
    public void A_backlog_past_the_limit_gets_one_library_scan()
    {
        FolderWatcherService.ShouldQueueFullScan(
            ingestedCount: 500,
            uningestedCount: FolderWatcherService.ReconcileFanOutLimit + 1).Should().BeTrue();
    }

    // The case reconciliation actually exists for: a handful of stragglers in a
    // library that is otherwise populated.
    [Fact]
    public void A_few_stragglers_still_fan_out_per_file()
    {
        FolderWatcherService.ShouldQueueFullScan(ingestedCount: 500, uningestedCount: 3).Should().BeFalse();
    }

    [Fact]
    public void The_limit_itself_still_fans_out()
    {
        FolderWatcherService.ShouldQueueFullScan(
            ingestedCount: 500,
            uningestedCount: FolderWatcherService.ReconcileFanOutLimit).Should().BeFalse();
    }
}
