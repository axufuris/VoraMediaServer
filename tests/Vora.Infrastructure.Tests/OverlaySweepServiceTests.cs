using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Settings;
using Vora.Infrastructure.Posters;

namespace Vora.Infrastructure.Tests;

// The sweep used to stat per URL: 48 File.Exists inside RemoveThumbnailsForSource
// plus one for the overlay file. Deleting a library of thousands of items meant
// hundreds of thousands of stat calls against a bind-mounted volume, which is
// what left "Delete Library" sitting there for minutes.
public class OverlaySweepServiceTests : IDisposable
{
    private readonly string _artworkDir;
    private readonly IArtworkThumbnailService _thumbnails;
    private readonly OverlaySweepService _sweep;

    public OverlaySweepServiceTests()
    {
        _artworkDir = Path.Combine(Path.GetTempPath(), "vora-overlay-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_artworkDir);

        _thumbnails = Substitute.For<IArtworkThumbnailService>();
        _sweep = new OverlaySweepService(
            Options.Create(new StoragePathsOptions { CustomArtwork = _artworkDir }),
            _thumbnails,
            NullLogger<OverlaySweepService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_artworkDir)) Directory.Delete(_artworkDir, recursive: true);
    }

    private string Touch(string fileName)
    {
        var path = Path.Combine(_artworkDir, fileName);
        File.WriteAllText(path, "");
        return path;
    }

    // The regression guard for the batching: one call carrying every source, not
    // one call per source.
    [Fact]
    public void Hands_every_source_to_the_thumbnail_cache_in_one_call()
    {
        _sweep.SweepPhysicalOverlays(new[] { "/a/one.jpg", "/a/two.jpg", null, "", "/a/three.jpg" });

        _thumbnails.Received(1).RemoveThumbnailsForSources(
            Arg.Is<IEnumerable<string?>>(s => s.Count() == 3),
            Arg.Any<CancellationToken>());
        _thumbnails.DidNotReceive().RemoveThumbnailsForSource(Arg.Any<string>());
    }

    [Fact]
    public void Does_not_touch_the_cache_when_there_is_nothing_to_sweep()
    {
        _sweep.SweepPhysicalOverlays(new string?[] { null, "" });

        _thumbnails.DidNotReceive().RemoveThumbnailsForSources(Arg.Any<IEnumerable<string?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Deletes_generated_overlay_files()
    {
        var overlay = Touch("abc_overlay_1.jpg");

        _sweep.SweepPhysicalOverlays(new[] { "/api/artwork/custom/abc_overlay_1.jpg" });

        File.Exists(overlay).Should().BeFalse();
    }

    // A user-uploaded poster has no overlay marker; deleting it would destroy
    // artwork the overlay pipeline never created.
    [Fact]
    public void Leaves_custom_artwork_that_is_not_a_generated_overlay()
    {
        var uploaded = Touch("user-poster.jpg");

        _sweep.SweepPhysicalOverlays(new[] { "/api/artwork/custom/user-poster.jpg" });

        File.Exists(uploaded).Should().BeTrue();
    }

    [Fact]
    public void Leaves_overlay_named_files_that_are_not_custom_artwork_urls()
    {
        var remote = Touch("remote_overlay_9.jpg");

        _sweep.SweepPhysicalOverlays(new[] { "https://example.com/remote_overlay_9.jpg" });

        File.Exists(remote).Should().BeTrue();
    }

    [Fact]
    public void Stops_when_cancelled()
    {
        Touch("abc_overlay_1.jpg");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sweep = () => _sweep.SweepPhysicalOverlays(new[] { "/api/artwork/custom/abc_overlay_1.jpg" }, cts.Token);

        sweep.Should().Throw<OperationCanceledException>();
    }
}
