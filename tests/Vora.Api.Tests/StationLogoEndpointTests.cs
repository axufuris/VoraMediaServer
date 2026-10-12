using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Api.Tests.Infra;
using Vora.Application.Artwork;
using Vora.Application.Net;

namespace Vora.Api.Tests;

public class StationLogoEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;
    private readonly ISafeImageDownloader _downloader = Substitute.For<ISafeImageDownloader>();

    public StationLogoEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private static byte[] Logo()
    {
        using var image = new Image<Rgba32>(300, 300, new Rgba32(20, 90, 200, 255));
        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private WebApplicationFactory<Program> Server()
    {
        _downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Logo());
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISafeImageDownloader>();
                services.AddSingleton(_downloader);
            }));
    }

    private static string ThumbnailOf(string source) => $"/api/artwork/thumb?w=360&kind=logo&src={Uri.EscapeDataString(source)}";

    [Fact]
    public async Task A_station_logo_the_server_sent_out_is_served_without_signing_in()
    {
        var source = $"http://www.181.fm/images/{Guid.NewGuid():N}.png";
        using var server = Server();
        using var client = server.CreateClient();
        server.Services.GetRequiredService<IRemoteImageRegistry>().Remember(source);

        using var response = await client.GetAsync(ThumbnailOf(source), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        await _downloader.Received(1).DownloadAsync(source, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_address_the_server_never_sent_out_is_not_fetched()
    {
        var source = $"http://www.181.fm/images/{Guid.NewGuid():N}.png";
        using var server = Server();
        using var client = server.CreateClient();

        using var response = await client.GetAsync(ThumbnailOf(source), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _downloader.DidNotReceive().DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
