using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Artwork;
using Vora.Application.Net;
using Vora.Application.Settings;
using Vora.Infrastructure.Artwork;

namespace Vora.Infrastructure.Tests;

// A host the allowlist does not name produces a BLANK image, not a broken one:
// the thumbnail route 404s and MediaCard shows its placeholder, while any page
// rendering the raw url in an <img> still displays the picture. Music artists
// showed exactly that split — a placeholder circle in the grid and the real
// photo on the artist's own page — because artwork from Fanart was listed and
// artwork from TheAudioDB was not.
//
// So the allowlist is not just a security boundary; it is a list every artwork
// provider has to appear on to be visible at all. These pin the hosts the
// shipped providers actually serve images from.
public class ArtworkThumbnailSourceAllowlistTests : IDisposable
{
    private readonly string _root;
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();
    private readonly RemoteImageRegistry _remoteImages = new();
    private readonly ILocalArtworkLocator _localArtwork = Substitute.For<ILocalArtworkLocator>();
    private readonly Clock _clock = new();
    private readonly ArtworkThumbnailService _service;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public ArtworkThumbnailSourceAllowlistTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vora-thumb-allowlist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _service = new ArtworkThumbnailService(
            _downloader,
            _remoteImages,
            Options.Create(new StoragePathsOptions { CustomArtwork = _root }),
            NullLogger<ArtworkThumbnailService>.Instance,
            _localArtwork,
            _clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    // Movies and shows.
    [InlineData("https://image.tmdb.org/t/p/w500/poster.jpg")]
    [InlineData("https://artworks.thetvdb.com/banners/series/1/posters/2.jpg")]
    // Music: Fanart answers for some artists, TheAudioDB for the rest, and
    // whichever one answers must not decide whether the image appears.
    [InlineData("https://assets.fanart.tv/fanart/music/abc/artistthumb/band.jpg")]
    [InlineData("https://www.theaudiodb.com/images/media/artist/thumb/band.jpg")]
    [InlineData("https://r2.theaudiodb.com/images/media/artist/thumb/band.jpg")]
    // Album covers, when neither Fanart nor an embedded tag had one.
    [InlineData("https://coverartarchive.org/release-group/abc/front-500.jpg")]
    public async Task An_artwork_providers_host_is_fetched(string source)
    {
        await _service.GetOrCreateThumbnailAsync(source, 360, "poster", TestContext.Current.CancellationToken);

        await _downloader.Received(1).DownloadAsync(source, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://example.com/poster.jpg")]
    [InlineData("https://image.tmdb.org.evil.test/poster.jpg")]
    [InlineData("ftp://image.tmdb.org/poster.jpg")]
    public async Task An_unlisted_host_is_never_fetched(string source)
    {
        var result = await _service.GetOrCreateThumbnailAsync(source, 360, "poster", TestContext.Current.CancellationToken);

        result.Should().BeNull();
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_station_logo_the_server_sent_to_a_client_is_fetched()
    {
        const string logo = "http://www.181.fm/images/181fm.png";
        _remoteImages.Remember(logo);

        await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken);

        await _downloader.Received(1).DownloadAsync(logo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_logo_that_could_not_be_fetched_is_left_alone_for_a_while()
    {
        const string logo = "http://echoesofbluemars.org/images/logo.jpg";
        _remoteImages.Remember(logo);
        _downloader.DownloadAsync(logo, Arg.Any<CancellationToken>()).Returns<byte[]>(_ => throw new HttpRequestException("Connection refused"));

        await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken);
        await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken);
        await _service.GetOrCreateThumbnailAsync(logo, 200, "logo", TestContext.Current.CancellationToken);

        await _downloader.Received(1).DownloadAsync(logo, Arg.Any<CancellationToken>());

        _clock.Now += ArtworkThumbnailService.FailedRemoteImageRetryAfter + TimeSpan.FromSeconds(1);
        await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken);

        await _downloader.Received(2).DownloadAsync(logo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_left_alone_for_a_while_too()
    {
        const string logo = "http://www.181.fm/favicon.ico";
        _remoteImages.Remember(logo);
        _downloader.DownloadAsync(logo, Arg.Any<CancellationToken>()).Returns(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

        (await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken)).Should().BeNull();
        (await _service.GetOrCreateThumbnailAsync(logo, 360, "logo", TestContext.Current.CancellationToken)).Should().BeNull();

        await _downloader.Received(1).DownloadAsync(logo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Another_address_on_the_same_host_is_not_fetched()
    {
        _remoteImages.Remember("http://www.181.fm/images/181fm.png");

        var result = await _service.GetOrCreateThumbnailAsync("http://www.181.fm/admin/export.png", 360, "logo", TestContext.Current.CancellationToken);

        result.Should().BeNull();
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
