using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Artwork;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;

namespace Vora.Infrastructure.Tests;

public class LocalArtworkThumbnailTests : IDisposable
{
    private static readonly string Address = $"{LocalArtworkAddress.Prefix}{Guid.NewGuid()}/poster.png?v=1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-local-thumb-" + Guid.NewGuid().ToString("N"));
    private readonly string _serverFolder;
    private readonly string _mediaFolder;
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();
    private readonly ILocalArtworkLocator _localArtwork = Substitute.For<ILocalArtworkLocator>();
    private readonly ArtworkThumbnailService _service;

    public LocalArtworkThumbnailTests()
    {
        _serverFolder = Path.Combine(_root, "server");
        _mediaFolder = Path.Combine(_root, "media", "Alien (1979)");
        Directory.CreateDirectory(_serverFolder);
        Directory.CreateDirectory(_mediaFolder);

        _service = new ArtworkThumbnailService(
            _downloader,
            new RemoteImageRegistry(),
            Options.Create(new StoragePathsOptions { CustomArtwork = _serverFolder }),
            NullLogger<ArtworkThumbnailService>.Instance,
            _localArtwork,
            Substitute.For<IVoraPosterCatalog>());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Poster(int width, int height)
    {
        var path = Path.Combine(_mediaFolder, "poster.png");
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30, 255));
        image.Save(path, new PngEncoder());
        return path;
    }

    [Fact]
    public async Task A_local_image_is_resized_straight_from_the_media_folder_and_only_the_small_copy_is_kept()
    {
        var original = Poster(1000, 1500);
        _localArtwork.ResolveAsync(Address).Returns(original);

        var thumbnail = await _service.GetOrCreateThumbnailAsync(Address, 360, "poster", TestContext.Current.CancellationToken);

        var cached = Assert.IsType<string>(thumbnail);
        cached.Should().StartWith(Path.Combine(_serverFolder, "imagecache"));
        using (var served = Image.Load(cached))
        {
            served.Width.Should().Be(360);
        }
        Directory.GetFiles(_serverFolder, "*", SearchOption.AllDirectories).Should().Equal(cached);
        Directory.GetFiles(_mediaFolder).Should().Equal(original);
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_media_folder_is_read_once_and_later_requests_come_from_the_cache()
    {
        _localArtwork.ResolveAsync(Address).Returns(Poster(1000, 1500));

        var first = await _service.GetOrCreateThumbnailAsync(Address, 360, "poster", TestContext.Current.CancellationToken);
        var second = await _service.GetOrCreateThumbnailAsync(Address, 360, "poster", TestContext.Current.CancellationToken);

        second.Should().Be(first);
        await _localArtwork.Received(1).ResolveAsync(Address);
    }

    [Fact]
    public async Task An_address_that_no_longer_leads_to_a_file_has_no_thumbnail()
    {
        _localArtwork.ResolveAsync(Address).Returns((string?)null);

        var thumbnail = await _service.GetOrCreateThumbnailAsync(Address, 360, "poster", TestContext.Current.CancellationToken);

        thumbnail.Should().BeNull();
    }
}
