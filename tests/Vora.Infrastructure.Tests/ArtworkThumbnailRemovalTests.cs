using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;

namespace Vora.Infrastructure.Tests;

// Removing one source probes 4 kind folders x 6 widths x 2 extensions = 48
// File.Exists calls, nearly all misses. Done per source across a whole library
// those stats dominate the delete, so the bulk path lists each kind folder once
// and tests membership instead.
public class ArtworkThumbnailRemovalTests : IDisposable
{
    private readonly string _root;
    private readonly ArtworkThumbnailService _service;

    private const int SomeWidth = 200;
    private const int AnotherWidth = 500;

    public ArtworkThumbnailRemovalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vora-thumb-removal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _service = new ArtworkThumbnailService(
            Substitute.For<ISafeImageDownloader>(),
            Options.Create(new StoragePathsOptions { CustomArtwork = _root }),
            NullLogger<ArtworkThumbnailService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string CachedFile(string kindFolder, string src, int width)
    {
        var dir = Path.Combine(_root, "imagecache", kindFolder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ArtworkThumbnailService.CacheKey(src, width) + ".jpg");
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Removes_every_cached_width_for_the_given_sources()
    {
        const string src = "https://image.tmdb.org/t/p/w500/poster.jpg";
        var small = CachedFile("posters", src, SomeWidth);
        var large = CachedFile("posters", src, AnotherWidth);

        _service.RemoveThumbnailsForSources(new[] { src });

        File.Exists(small).Should().BeFalse();
        File.Exists(large).Should().BeFalse();
    }

    [Fact]
    public void Removes_across_every_kind_folder()
    {
        const string src = "https://example.com/art.jpg";
        var poster = CachedFile("posters", src, SomeWidth);
        var backdrop = CachedFile("backdrops", src, SomeWidth);
        var still = CachedFile("stills", src, SomeWidth);

        _service.RemoveThumbnailsForSources(new[] { src });

        File.Exists(poster).Should().BeFalse();
        File.Exists(backdrop).Should().BeFalse();
        File.Exists(still).Should().BeFalse();
    }

    [Fact]
    public void Leaves_cached_thumbnails_of_other_sources_alone()
    {
        var doomed = CachedFile("posters", "https://example.com/doomed.jpg", SomeWidth);
        var keep = CachedFile("posters", "https://example.com/keep.jpg", SomeWidth);

        _service.RemoveThumbnailsForSources(new[] { "https://example.com/doomed.jpg" });

        File.Exists(doomed).Should().BeFalse();
        File.Exists(keep).Should().BeTrue();
    }

    [Fact]
    public void Handles_many_sources_in_one_pass()
    {
        var sources = Enumerable.Range(0, 50).Select(i => $"https://example.com/{i}.jpg").ToList();
        var files = sources.Select(s => CachedFile("posters", s, SomeWidth)).ToList();

        _service.RemoveThumbnailsForSources(sources);

        files.Should().OnlyContain(f => !File.Exists(f));
    }

    [Fact]
    public void Ignores_null_and_empty_sources()
    {
        var act = () => _service.RemoveThumbnailsForSources(new string?[] { null, "", "   " });

        act.Should().NotThrow();
    }

    [Fact]
    public void Stops_when_cancelled()
    {
        CachedFile("posters", "https://example.com/a.jpg", SomeWidth);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _service.RemoveThumbnailsForSources(new[] { "https://example.com/a.jpg" }, cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}
