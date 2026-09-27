using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Api.Tests.Infra;
using Vora.Application.Net;

namespace Vora.Api.Tests;

public class LogoThumbnailEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private const string LogoSource = "https://image.tmdb.org/t/p/original/wordmark.png";

    private readonly VoraApiTestFactory _factory;

    public LogoThumbnailEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    // Half opaque wordmark, half transparent — so a JPEG round-trip is visible
    // in the pixels rather than only in the header.
    private static byte[] TransparentLogoPng()
    {
        using var image = new Image<Rgba32>(600, 240, new Rgba32(0, 0, 0, 0));
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width / 2; x++)
            {
                image[x, y] = new Rgba32(255, 255, 255, 255);
            }
        }

        using var buffer = new MemoryStream();
        image.Save(buffer, new PngEncoder());
        return buffer.ToArray();
    }

    private HttpClient ClientWithStubbedArtwork()
    {
        var downloader = Substitute.For<ISafeImageDownloader>();
        downloader.DownloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TransparentLogoPng());

        var client = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISafeImageDownloader>();
                services.AddSingleton(downloader);
            })).CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid()));
        return client;
    }

    [Fact]
    public async Task A_logo_thumbnail_is_served_as_a_png_with_its_transparency_intact()
    {
        using var client = ClientWithStubbedArtwork();

        using var response = await client.GetAsync($"/api/artwork/thumb?w=780&kind=logo&src={Uri.EscapeDataString(LogoSource)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        using var served = Image.Load<Rgba32>(bytes);

        served[served.Width - 2, served.Height / 2].A.Should().Be(0, "the transparent half must survive the cache");
        served[2, served.Height / 2].A.Should().Be(255, "the wordmark must not be flattened onto black");
    }

    [Fact]
    public async Task A_poster_thumbnail_is_still_served_as_jpeg()
    {
        using var client = ClientWithStubbedArtwork();

        using var response = await client.GetAsync($"/api/artwork/thumb?w=500&kind=poster&src={Uri.EscapeDataString("https://image.tmdb.org/t/p/original/poster.jpg")}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }
}
