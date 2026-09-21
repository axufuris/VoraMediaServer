using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Plugins;

// The polling watcher infers deletions by diffing successive listings. When a
// listing is incomplete — one unreadable folder on a NAS — every unseen file
// looks deleted, which queued a cleanup for the whole library on a single blip.
public class PollingWatcherChangeResolutionTests
{
    private static HashSet<string> Set(params string[] paths) =>
        new(paths, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_complete_pass_reports_additions_and_deletions()
    {
        var current = Set("/m/kept.mkv", "/m/new.mkv");
        var previous = Set("/m/kept.mkv", "/m/gone.mkv");

        var (added, deleted, known) = PollingFolderWatcherProvider.ResolveChanges(current, previous, complete: true);

        added.Should().BeEquivalentTo(new[] { "/m/new.mkv" });
        deleted.Should().BeEquivalentTo(new[] { "/m/gone.mkv" });
        known.Should().BeEquivalentTo(current);
    }

    [Fact]
    public void An_incomplete_pass_reports_no_deletions()
    {
        var current = Set("/m/kept.mkv");
        var previous = Set("/m/kept.mkv", "/m/unseen.mkv");

        var (_, deleted, _) = PollingFolderWatcherProvider.ResolveChanges(current, previous, complete: false);

        deleted.Should().BeEmpty();
    }

    [Fact]
    public void An_incomplete_pass_that_saw_nothing_at_all_still_reports_no_deletions()
    {
        var previous = Set("/m/a.mkv", "/m/b.mkv", "/m/c.mkv");

        var (added, deleted, known) = PollingFolderWatcherProvider.ResolveChanges(Set(), previous, complete: false);

        added.Should().BeEmpty();
        deleted.Should().BeEmpty();
        known.Should().BeEquivalentTo(previous);
    }

    // A path that turned up really is there, whatever else the pass missed.
    [Fact]
    public void An_incomplete_pass_still_reports_additions()
    {
        var current = Set("/m/kept.mkv", "/m/new.mkv");
        var previous = Set("/m/kept.mkv", "/m/unseen.mkv");

        var (added, _, _) = PollingFolderWatcherProvider.ResolveChanges(current, previous, complete: false);

        added.Should().BeEquivalentTo(new[] { "/m/new.mkv" });
    }

    // Replacing rather than unioning would make the unseen file read as a
    // deletion on the next complete pass — the blip deferred, not avoided.
    [Fact]
    public void An_incomplete_pass_keeps_unseen_files_known()
    {
        var previous = Set("/m/kept.mkv", "/m/unseen.mkv");

        var (_, _, known) = PollingFolderWatcherProvider.ResolveChanges(Set("/m/kept.mkv", "/m/new.mkv"), previous, complete: false);

        known.Should().BeEquivalentTo(new[] { "/m/kept.mkv", "/m/new.mkv", "/m/unseen.mkv" });

        var (_, deleted, _) = PollingFolderWatcherProvider.ResolveChanges(
            Set("/m/kept.mkv", "/m/new.mkv", "/m/unseen.mkv"), known, complete: true);

        deleted.Should().BeEmpty();
    }

    [Fact]
    public void A_file_genuinely_removed_is_still_reported_once_a_complete_pass_confirms_it()
    {
        var previous = Set("/m/kept.mkv", "/m/gone.mkv");

        var (_, _, known) = PollingFolderWatcherProvider.ResolveChanges(Set("/m/kept.mkv"), previous, complete: false);
        var (_, deleted, _) = PollingFolderWatcherProvider.ResolveChanges(Set("/m/kept.mkv"), known, complete: true);

        deleted.Should().BeEquivalentTo(new[] { "/m/gone.mkv" });
    }
}
