using Microsoft.Extensions.Logging.Abstractions;
using Vora.Infrastructure.FileSystem;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.Tests;

// The reconciliation sweep must keep going when a folder cannot be read. Its
// original guard was a try/catch around a LAZY Directory.EnumerateFiles, so it
// caught nothing — the enumeration ran after the catch had gone out of scope and
// the exception escaped into ReconcileLibraryAsync, abandoning the whole sweep.
// It looked guarded and was not, which is why these assert behaviour rather than
// trusting the call to route through ResilientDirectory.
public class FolderWatcherEnumerationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FolderWatcherService _service;

    public FolderWatcherEnumerationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "vora-watcher-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _service = new FolderWatcherService(
            Substitute.For<IServiceProvider>(),
            NullLogger<FolderWatcherService>.Instance,
            Array.Empty<IFolderWatcherProvider>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private string Touch(string relative)
    {
        var full = Path.Combine(_tempRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
        return full;
    }

    [Fact]
    public void Finds_files_at_every_depth()
    {
        var root = Touch("root.mkv");
        var nested = Touch(Path.Combine("Show", "Season 01", "episode.mkv"));

        _service.EnumerateSupportedFiles(_tempRoot).Should().BeEquivalentTo(new[] { root, nested });
    }

    // A contract assertion, not a regression guard: .NET validates a missing
    // directory eagerly, so the old try/catch caught this one too.
    [Fact]
    public void Returns_empty_rather_than_throwing_for_a_missing_directory()
    {
        var missing = Path.Combine(_tempRoot, "does-not-exist");

        var enumerate = () => _service.EnumerateSupportedFiles(missing).ToList();

        enumerate.Should().NotThrow();
        enumerate().Should().BeEmpty();
    }

    // This is the test that actually pins the fix. A file path is rejected during
    // ITERATION rather than eagerly, so it escapes a try/catch that wraps only
    // the call — verified by reverting the implementation, where this is the one
    // of the three that fails.
    [Fact]
    public void Survives_iteration_when_the_root_is_a_file_not_a_directory()
    {
        var file = Touch("notadirectory.mkv");

        var enumerate = () => _service.EnumerateSupportedFiles(file).ToList();

        enumerate.Should().NotThrow();
        enumerate().Should().BeEmpty();
    }
}
