using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;
using Vora.Plugins.Providers.TheAudioDb;

namespace Vora.Application.Tests.Media;

// A misspelled json field is a silent no-op: the property is absent, nothing is
// added, and the slot stays empty exactly as it would for an album the provider
// has never heard of. `strAlbumThumbBack` was read for as long as it existed and
// TheAudioDB has never returned a field by that name — the real one is
// `strAlbumBack` — so back covers were never once offered, and nothing said so.
//
// These pin the field names against recorded payloads shaped like the live API.
public class TheAudioDbArtworkFieldTests
{
    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private static TheAudioDbMusicArtworkProvider ProviderReturning(string body)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new CannedHandler(body)));

        var settings = Substitute.For<IPluginSettingsProvider>();
        settings.GetSettingAsync(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IPluginSettingsProvider)).Returns(settings);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(services);
        scopeFactory.CreateScope().Returns(scope);

        return new TheAudioDbMusicArtworkProvider(factory, scopeFactory, NullLogger<TheAudioDbMusicArtworkProvider>.Instance);
    }

    // Field names and their population match a live searchalbum.php response.
    private const string AlbumPayload = """
    {"album":[{
        "strAlbum": "Grassroots",
        "strAlbumThumb": "https://r2.theaudiodb.com/images/media/album/thumb/front.jpg",
        "strAlbumThumbHQ": null,
        "strAlbumBack": "https://r2.theaudiodb.com/images/media/album/back/back.jpg",
        "strAlbumSpine": null,
        "strAlbum3DCase": "https://r2.theaudiodb.com/images/media/album/3dcase/case.png",
        "strAlbum3DFlat": "https://r2.theaudiodb.com/images/media/album/3dflat/flat.png",
        "strAlbum3DThumb": "https://r2.theaudiodb.com/images/media/album/3dthumb/thumb.png",
        "strAlbumCDart": "https://r2.theaudiodb.com/images/media/album/cdart/disc.png"
    }]}
    """;

    private const string ArtistPayload = """
    {"artists":[{
        "strArtist": "311",
        "strArtistThumb": "https://r2.theaudiodb.com/images/media/artist/thumb/photo.jpg",
        "strArtistCutout": null,
        "strArtistClearart": null,
        "strArtistLogo": "https://r2.theaudiodb.com/images/media/artist/logo/logo.png",
        "strArtistBanner": "https://r2.theaudiodb.com/images/media/artist/banner/banner.jpg",
        "strArtistWideThumb": "https://r2.theaudiodb.com/images/media/artist/widethumb/wide.jpg",
        "strArtistFanart": "https://r2.theaudiodb.com/images/media/artist/fanart/fanart.jpg",
        "strArtistFanart2": "https://r2.theaudiodb.com/images/media/artist/fanart/fanart2.jpg",
        "strArtistFanart3": null,
        "strArtistFanart4": null
    }]}
    """;

    [Fact]
    public async Task An_albums_back_cover_is_read_from_the_field_the_api_actually_returns()
    {
        var results = await ProviderReturning(AlbumPayload)
            .SearchAlbumArtworkAsync("311", "Grassroots", TestContext.Current.CancellationToken);

        results.Select(r => r.Url).Should().Contain("https://r2.theaudiodb.com/images/media/album/back/back.jpg");
    }

    [Fact]
    public async Task Every_album_image_the_api_supplies_is_offered()
    {
        var results = await ProviderReturning(AlbumPayload)
            .SearchAlbumArtworkAsync("311", "Grassroots", TestContext.Current.CancellationToken);

        results.Should().HaveCount(6, "the payload sets six image fields and none should be dropped");
    }

    [Fact]
    public async Task Disc_art_is_classified_apart_from_the_cover()
    {
        var results = await ProviderReturning(AlbumPayload)
            .SearchAlbumArtworkAsync("311", "Grassroots", TestContext.Current.CancellationToken);

        results.Single(r => r.Kind == MusicArtworkKind.Logo).Url
            .Should().Be("https://r2.theaudiodb.com/images/media/album/cdart/disc.png");
    }

    // The flat front cover has to come first, because an automatic fill takes the
    // first result of the kind it wants — a 3D case render as the album cover is
    // a worse default than the cover itself.
    [Fact]
    public async Task The_flat_front_cover_is_the_first_cover_offered()
    {
        var results = await ProviderReturning(AlbumPayload)
            .SearchAlbumArtworkAsync("311", "Grassroots", TestContext.Current.CancellationToken);

        results.First(r => r.Kind == MusicArtworkKind.Cover).Url
            .Should().Be("https://r2.theaudiodb.com/images/media/album/thumb/front.jpg");
    }

    // A widethumb is a ~5:1 strip. Taking it as the backdrop is what filled the
    // artist header with a sliver of a photo, so the 16:9 fanart has to win.
    [Fact]
    public async Task A_full_fanart_outranks_the_wide_strip_for_the_background()
    {
        var results = await ProviderReturning(ArtistPayload)
            .SearchArtistArtworkAsync("311", TestContext.Current.CancellationToken);

        var backgrounds = results.Where(r => r.Kind == MusicArtworkKind.Background).Select(r => r.Url).ToList();

        backgrounds.First().Should().Be("https://r2.theaudiodb.com/images/media/artist/fanart/fanart.jpg");
        backgrounds.Should().Contain("https://r2.theaudiodb.com/images/media/artist/widethumb/wide.jpg",
            "it stays available for artists with no fanart at all");
        backgrounds.Last().Should().Be("https://r2.theaudiodb.com/images/media/artist/widethumb/wide.jpg");
    }

    [Fact]
    public async Task The_banner_and_logo_keep_their_own_kinds()
    {
        var results = await ProviderReturning(ArtistPayload)
            .SearchArtistArtworkAsync("311", TestContext.Current.CancellationToken);

        results.Single(r => r.Kind == MusicArtworkKind.Banner).Url
            .Should().Be("https://r2.theaudiodb.com/images/media/artist/banner/banner.jpg");
        results.Single(r => r.Kind == MusicArtworkKind.Logo).Url
            .Should().Be("https://r2.theaudiodb.com/images/media/artist/logo/logo.png");
        results.Single(r => r.Kind == MusicArtworkKind.Thumb).Url
            .Should().Be("https://r2.theaudiodb.com/images/media/artist/thumb/photo.jpg");
    }
}
