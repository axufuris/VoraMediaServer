using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Artwork;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;

namespace Vora.Infrastructure.Tests;

public sealed class VoraPosterThumbnailTests : IDisposable
{
    private const string Address = "/api/artwork/vora/holidays/halloween.jpg?v=abc";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-poster-thumbs-" + Guid.NewGuid().ToString("N"));
    private readonly IVoraPosterCatalog _posters = Substitute.For<IVoraPosterCatalog>();
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();
    private readonly ArtworkThumbnailService _service;

    public VoraPosterThumbnailTests()
    {
        Directory.CreateDirectory(_root);
        _service = new ArtworkThumbnailService(
            _downloader,
            new RemoteImageRegistry(),
            Options.Create(new StoragePathsOptions { CustomArtwork = Path.Combine(_root, "server") }),
            NullLogger<ArtworkThumbnailService>.Instance,
            Substitute.For<ILocalArtworkLocator>(),
            _posters);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task A_vora_poster_is_resized_from_the_file_that_ships_with_the_server()
    {
        var file = Path.Combine(_root, "halloween.jpg");
        using (var image = new Image<Rgba32>(1000, 1500, new Rgba32(200, 90, 20, 255)))
        {
            await image.SaveAsync(file, new JpegEncoder(), TestContext.Current.CancellationToken);
        }
        _posters.Find(Address).Returns(new VoraPoster("halloween", "holidays", "Halloween", ["halloween"], Address, file));

        var thumbnail = await _service.GetOrCreateThumbnailAsync(Address, 360, "poster", TestContext.Current.CancellationToken);

        thumbnail.Should().NotBeNull();
        using var resized = await Image.LoadAsync(thumbnail ?? string.Empty, TestContext.Current.CancellationToken);
        resized.Width.Should().Be(360);
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_address_that_names_no_shipped_poster_has_no_thumbnail()
    {
        var thumbnail = await _service.GetOrCreateThumbnailAsync("/api/artwork/vora/holidays/gone.jpg?v=1", 360, "poster", TestContext.Current.CancellationToken);

        thumbnail.Should().BeNull();
    }
}
