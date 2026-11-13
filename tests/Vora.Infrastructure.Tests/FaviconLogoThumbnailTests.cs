using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Application.Artwork;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;
using Xunit;

namespace Vora.Infrastructure.Tests;

// Plenty of radio stations' logos are the site's favicon. A Roku can't decode
// an .ico, so the logo goes through the image cache, which has to turn it into
// one plain PNG of the largest icon in the file.
public class FaviconLogoThumbnailTests : IDisposable
{
    private const string Favicon = "https://www.iheart.com/favicon.ico";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-favicon-cache-" + Guid.NewGuid().ToString("N"));
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();
    private readonly RemoteImageRegistry _registry = new();
    private readonly ArtworkThumbnailService _service;

    public FaviconLogoThumbnailTests()
    {
        Directory.CreateDirectory(_root);
        _service = new ArtworkThumbnailService(
            _downloader,
            _registry,
            Options.Create(new StoragePathsOptions { CustomArtwork = _root }),
            NullLogger<ArtworkThumbnailService>.Instance,
            Substitute.For<ILocalArtworkLocator>(),
            Substitute.For<IVoraPosterCatalog>());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // A classic favicon: each size a 32-bit bitmap (no PNG inside), smallest
    // first, as iheart.com's and my-radios.com's are.
    private static byte[] Favicon16And32()
    {
        var icons = new[] { Bitmap(16, red: 255, blue: 0), Bitmap(32, red: 0, blue: 255) };
        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)icons.Length);
        var offset = 6 + 16 * icons.Length;
        foreach (var (side, data) in icons)
        {
            w.Write((byte)side);
            w.Write((byte)side);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in icons) w.Write(data);
        return file.ToArray();
    }

    private static (int Side, byte[] Data) Bitmap(int side, byte red, byte blue)
    {
        using var bmp = new MemoryStream();
        using var w = new BinaryWriter(bmp);
        w.Write(40);
        w.Write(side);
        w.Write(side * 2); // the colour rows and the mask's
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        for (var i = 0; i < side * side; i++)
        {
            w.Write(blue);
            w.Write((byte)0);
            w.Write(red);
            w.Write((byte)255);
        }
        var maskRow = (side + 31) / 32 * 4;
        w.Write(new byte[maskRow * side]);
        return (side, bmp.ToArray());
    }

    [Fact]
    public void The_downloader_takes_an_ico_for_an_image()
    {
        SafeImageDownloader.LooksLikeImage(Favicon16And32()).Should().BeTrue();
    }

    [Fact]
    public async Task A_favicon_logo_becomes_one_png_of_its_largest_icon()
    {
        _registry.Remember(Favicon);
        _downloader.DownloadAsync(Favicon, Arg.Any<CancellationToken>()).Returns(Favicon16And32());

        var path = await _service.GetOrCreateThumbnailAsync(Favicon, 360, "logo", TestContext.Current.CancellationToken);

        path.Should().NotBeNull();
        Path.GetExtension(path).Should().Be(".png");
        using var cached = await Image.LoadAsync<Rgba32>(path!, TestContext.Current.CancellationToken);
        cached.Frames.Count.Should().Be(1);
        cached.Width.Should().Be(32);
        cached.Height.Should().Be(32);
        // The 32 px icon (blue) fills the square: not the 16 px one (red) in a corner.
        cached[cached.Width - 2, cached.Height - 2].Should().Be(new Rgba32(0, 0, 255, 255));
        cached[1, 1].Should().Be(new Rgba32(0, 0, 255, 255));
    }

    [Fact]
    public async Task An_https_logo_the_server_never_sent_out_is_not_fetched()
    {
        var path = await _service.GetOrCreateThumbnailAsync("https://elsewhere.example/favicon.ico", 360, "logo", TestContext.Current.CancellationToken);

        path.Should().BeNull();
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
