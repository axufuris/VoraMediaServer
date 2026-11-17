using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vora.Api.Tests.Infra;
using Vora.Application.Artwork;
using Vora.Application.Settings;
using Vora.Application.Thumbnails;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class ExtraThumbnailEndpointTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private const string BackdropUrl = "https://image.example/backdrop.jpg";

    private readonly VoraApiTestFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vora-extra-thumbnails-" + Guid.NewGuid().ToString("N"));
    private readonly IVideoThumbnailGeneratorService _generator = Substitute.For<IVideoThumbnailGeneratorService>();
    private readonly IArtworkThumbnailService _backdrops = Substitute.For<IArtworkThumbnailService>();
    private readonly Queue<Rgb24> _frames = new();

    public ExtraThumbnailEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
        Directory.CreateDirectory(_root);
        _generator.ProbeDurationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TimeSpan.FromSeconds(90));
        _generator.ExtractFrameAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (_frames.Count == 0) return false;
                using var image = new Image<Rgb24>(1920, 1080, _frames.Dequeue());
                image.SaveAsJpeg((string)call[2]);
                return true;
            });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private WebApplicationFactory<Program> Host() => _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IVideoThumbnailGeneratorService>();
        services.AddSingleton(_generator);
        services.RemoveAll<IArtworkThumbnailService>();
        services.AddSingleton(_backdrops);
        services.Configure<StoragePathsOptions>(options => options.CustomArtwork = Path.Combine(_root, "artwork"));
    }));

    private string VideoFile(string name)
    {
        var file = Path.Combine(_root, "Movie", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file) ?? _root);
        File.WriteAllText(file, string.Empty);
        return file;
    }

    private static async Task<Guid> SeedExtraAsync(WebApplicationFactory<Program> host, string? filePath, string? backdropUrl)
    {
        var movie = new Movie { Id = Guid.NewGuid(), Title = "The Movie", BackgroundUrl = backdropUrl };
        var extra = new MediaExtra { Id = Guid.NewGuid(), Title = "Teaser", ExtraType = MediaExtraType.Trailer, MediaItemId = movie.Id };
        if (filePath != null) extra.Parts.Add(new MediaPart { Id = Guid.NewGuid(), FilePath = filePath, Duration = TimeSpan.FromSeconds(90), MediaExtraId = extra.Id });

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.Add(movie);
        db.Add(extra);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return extra.Id;
    }

    [Fact]
    public async Task An_extra_that_does_not_exist_is_not_found()
    {
        using var client = Host().CreateClient();

        using var response = await client.GetAsync($"/api/artwork/extra/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_frame_from_the_extras_own_file_is_served_to_anyone_and_kept_for_good()
    {
        using var host = Host();
        var extraId = await SeedExtraAsync(host, VideoFile("Teaser.mkv"), BackdropUrl);
        _frames.Enqueue(new Rgb24(220, 180, 120));
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/extra/{extraId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        response.Headers.CacheControl?.ToString().Should().Contain("immutable");
        using var picture = Image.Load<Rgb24>(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        picture.Size.Should().Be(new Size(960, 540));
        await _backdrops.DidNotReceive().GetOrCreateThumbnailAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Two_extras_of_one_title_get_two_different_thumbnails()
    {
        using var host = Host();
        var teaser = await SeedExtraAsync(host, VideoFile("Teaser.mkv"), BackdropUrl);
        var featurette = await SeedExtraAsync(host, VideoFile("Making.of.mkv"), BackdropUrl);
        _frames.Enqueue(new Rgb24(220, 180, 120));
        _frames.Enqueue(new Rgb24(40, 120, 200));
        using var client = host.CreateClient();

        var first = await client.GetByteArrayAsync($"/api/artwork/extra/{teaser}", TestContext.Current.CancellationToken);
        var second = await client.GetByteArrayAsync($"/api/artwork/extra/{featurette}", TestContext.Current.CancellationToken);

        first.Should().NotEqual(second);
    }

    [Fact]
    public async Task With_no_frame_to_take_the_titles_backdrop_is_served_for_a_short_while()
    {
        using var host = Host();
        var extraId = await SeedExtraAsync(host, Path.Combine(_root, "Movie", "Gone.mkv"), BackdropUrl);
        var cached = Path.Combine(_root, "backdrop-780.jpg");
        using (var image = new Image<Rgb24>(780, 439, new Rgb24(10, 90, 60))) image.SaveAsJpeg(cached);
        _backdrops.GetOrCreateThumbnailAsync(BackdropUrl, 780, "backdrop", Arg.Any<CancellationToken>()).Returns(cached);
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/extra/{extraId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl?.ToString().Should().Contain("max-age=300").And.NotContain("immutable");
        using var picture = Image.Load<Rgb24>(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        picture.Size.Should().Be(new Size(780, 439));
    }

    [Fact]
    public async Task With_neither_a_frame_nor_a_backdrop_there_is_no_thumbnail()
    {
        using var host = Host();
        var extraId = await SeedExtraAsync(host, Path.Combine(_root, "Movie", "Gone.mkv"), null);
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/extra/{extraId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
