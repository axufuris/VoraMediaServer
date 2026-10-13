using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public sealed class LocalArtworkEndpointTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private static readonly byte[] PosterBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private readonly VoraApiTestFactory _factory;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vora-local-art-endpoint-" + Guid.NewGuid().ToString("N"), "Alien (1979)");

    public LocalArtworkEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_folder);
        if (root != null && Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private async Task<Guid> SeedMovieAsync()
    {
        var video = Path.Combine(_folder, "Alien (1979).mkv");
        await File.WriteAllBytesAsync(video, [0], TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(_folder, "poster.jpg"), PosterBytes, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_folder, "movie.nfo"), "<movie><title>Alien</title></movie>", TestContext.Current.CancellationToken);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var library = new MediaLibrary { Name = "Movies", Type = LibraryType.Movie, FolderPaths = [Path.GetDirectoryName(_folder) ?? _folder] };
        var movie = new Movie { Title = "Alien", LibraryId = library.Id };
        db.AddRange(library, movie, new MediaPart { FilePath = video, MediaItemId = movie.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return movie.Id;
    }

    [Fact]
    public async Task A_titles_local_poster_is_served_from_its_media_folder_without_signing_in()
    {
        var movieId = await SeedMovieAsync();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/local/{movieId}/poster.jpg?v=1", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().Equal(PosterBytes);
    }

    [Theory]
    [InlineData("movie.nfo")]
    [InlineData("Alien (1979).mkv")]
    [InlineData("fanart.jpg")]
    [InlineData("..%2Fposter.jpg")]
    public async Task Nothing_else_in_the_media_folder_can_be_fetched(string fileName)
    {
        var movieId = await SeedMovieAsync();
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/local/{movieId}/{fileName}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_title_has_no_local_artwork()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/api/artwork/local/{Guid.NewGuid()}/poster.jpg", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
