using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class LogoThumbnailCacheTests : IDisposable
{
    private const string LogoSource = "https://image.tmdb.org/t/p/original/logo.png";
    private const string PosterSource = "https://image.tmdb.org/t/p/original/poster.jpg";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-logo-cache-" + Guid.NewGuid().ToString("N"));
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();
    private readonly ArtworkThumbnailService _service;

    public LogoThumbnailCacheTests()
    {
        Directory.CreateDirectory(_root);
        _service = new ArtworkThumbnailService(
            _downloader,
            Options.Create(new StoragePathsOptions { CustomArtwork = _root }),
            NullLogger<ArtworkThumbnailService>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // A wordmark: opaque pixels on the left, fully transparent on the right.
    private static byte[] TransparentLogoPng(int width = 800, int height = 320)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width / 2; x++)
            {
                image[x, y] = new Rgba32(255, 255, 255, 255);
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private static byte[] OpaqueImage(int width = 800, int height = 320, bool png = true)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30, 255));
        using var buffer = new MemoryStream();
        if (png) image.Save(buffer, new PngEncoder());
        else image.Save(buffer, new JpegEncoder());
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_logo_is_cached_as_a_png_that_still_has_its_transparency()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var path = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);

        path.Should().NotBeNull();
        Path.GetExtension(path).Should().Be(".png");

        using var cached = await Image.LoadAsync<Rgba32>(path!, TestContext.Current.CancellationToken);
        cached.Width.Should().Be(780);

        // The right-hand half was transparent and must still be.
        cached[cached.Width - 2, cached.Height / 2].A.Should().Be(0);
        // The left-hand half was the wordmark and must still be opaque, not black.
        var mark = cached[2, cached.Height / 2];
        mark.A.Should().Be(255);
        mark.R.Should().BeGreaterThan(200);
    }

    [Fact]
    public async Task The_cached_logo_lands_in_its_own_bucket()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var path = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);

        Path.GetFileName(Path.GetDirectoryName(path)).Should().Be("logos");
    }

    [Fact]
    public async Task A_second_request_is_served_from_the_cache_without_fetching_again()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var first = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);
        var second = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);

        second.Should().Be(first);
        await _downloader.Received(1).DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // The container follows the kind, not the pixels: a logo that happens to be
    // opaque today must not be cached as a JPEG that cannot hold the alpha of
    // the next one served from the same bucket.
    [Fact]
    public async Task Every_logo_is_cached_as_a_png_even_when_this_one_is_opaque()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OpaqueImage());

        var path = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);

        Path.GetExtension(path).Should().Be(".png");
    }

    [Fact]
    public async Task The_cached_logo_file_really_carries_an_alpha_channel()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var path = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);

        using var cached = await Image.LoadAsync<Rgba32>(path!, TestContext.Current.CancellationToken);
        var metadata = cached.Metadata.GetPngMetadata();

        metadata.ColorType.Should().Be(PngColorType.RgbWithAlpha);
        Image.DetectFormat(path!).Name.Should().Be("PNG");
    }

    [Fact]
    public async Task A_poster_is_unaffected_and_stays_jpeg()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(OpaqueImage(png: false));

        var path = await _service.GetOrCreateThumbnailAsync(PosterSource, 500, "poster", TestContext.Current.CancellationToken);

        Path.GetExtension(path).Should().Be(".jpg");
        Path.GetFileName(Path.GetDirectoryName(path)).Should().Be("posters");
    }

    [Fact]
    public async Task A_transparent_source_asked_for_as_a_poster_is_flattened_as_before()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var path = await _service.GetOrCreateThumbnailAsync(PosterSource, 500, "poster", TestContext.Current.CancellationToken);

        Path.GetExtension(path).Should().Be(".jpg");
    }

    [Fact]
    public async Task Removing_a_source_clears_the_png_it_was_cached_as()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());
        var path = await _service.GetOrCreateThumbnailAsync(LogoSource, 780, "logo", TestContext.Current.CancellationToken);
        File.Exists(path).Should().BeTrue();

        _service.RemoveThumbnailsForSource(LogoSource);

        File.Exists(path).Should().BeFalse();
    }
}
