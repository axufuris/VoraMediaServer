using Vora.Plugins.Providers.Local;

namespace Vora.Application.Tests.Scanner;

// Directory.GetFiles(root, "*.*", AllDirectories) throws on the first unreadable
// subdirectory and yields nothing for the whole tree, so one bad folder on a
// network share made a populated library scan as though it were empty. These
// cover the hand-rolled walk that replaced it.
public class ResilientEnumerationTests
{
    private static readonly string[] None = Array.Empty<string>();

    private static IEnumerable<string> Walk(
        string root,
        Dictionary<string, string[]> directories,
        Dictionary<string, string[]> files,
        Action<string, Exception>? onSkipped = null)
    {
        return VoraLocalMediaScannerProvider.EnumerateFilesResiliently(
            root,
            dir => directories.TryGetValue(dir, out var d) ? d : throw new UnauthorizedAccessException(dir),
            dir => files.TryGetValue(dir, out var f) ? f : throw new IOException(dir),
            onSkipped ?? ((_, _) => { }));
    }

    [Fact]
    public void Returns_files_from_every_depth()
    {
        var directories = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/a" },
            ["/m/a"] = new[] { "/m/a/b" },
            ["/m/a/b"] = None,
        };
        var files = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/root.mkv" },
            ["/m/a"] = new[] { "/m/a/mid.mkv" },
            ["/m/a/b"] = new[] { "/m/a/b/deep.mkv" },
        };

        Walk("/m", directories, files).Should().BeEquivalentTo(
            new[] { "/m/root.mkv", "/m/a/mid.mkv", "/m/a/b/deep.mkv" });
    }

    [Fact]
    public void An_unreadable_directory_costs_only_that_directory()
    {
        var directories = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/good", "/m/denied" },
            ["/m/good"] = None,
            // "/m/denied" absent from both maps, so listing it throws.
        };
        var files = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/root.mkv" },
            ["/m/good"] = new[] { "/m/good/keep.mkv" },
        };

        Walk("/m", directories, files).Should().BeEquivalentTo(
            new[] { "/m/root.mkv", "/m/good/keep.mkv" });
    }

    [Fact]
    public void A_directory_whose_files_cannot_be_listed_still_has_its_subdirectories_walked()
    {
        var directories = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/a" },
            ["/m/a"] = new[] { "/m/a/b" },
            ["/m/a/b"] = None,
        };
        var files = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/root.mkv" },
            // "/m/a" absent, so listing its files throws.
            ["/m/a/b"] = new[] { "/m/a/b/deep.mkv" },
        };

        Walk("/m", directories, files).Should().BeEquivalentTo(
            new[] { "/m/root.mkv", "/m/a/b/deep.mkv" });
    }

    // A scan that silently finds nothing is indistinguishable from an empty
    // library, which is the failure this guard exists to prevent.
    [Fact]
    public void Reports_every_directory_it_skips()
    {
        var skipped = new List<string>();

        var directories = new Dictionary<string, string[]>
        {
            ["/m"] = new[] { "/m/denied" },
        };
        var files = new Dictionary<string, string[]>
        {
            ["/m"] = None,
        };

        Walk("/m", directories, files, (dir, _) => skipped.Add(dir)).ToList();

        skipped.Should().Contain("/m/denied");
    }

    [Fact]
    public void An_unreadable_root_yields_nothing_rather_than_throwing()
    {
        var walk = () => Walk("/m", new Dictionary<string, string[]>(), new Dictionary<string, string[]>()).ToList();

        walk.Should().NotThrow();
        walk().Should().BeEmpty();
    }
}
