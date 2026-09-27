using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class MusicAlbumsEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public MusicAlbumsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SeedLibraryAsync(params string[] titles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Albums", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Seeded Artist", LibraryId = library.Id };
        db.AddRange(library, artist);
        var addedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var title in titles)
        {
            addedAt = addedAt.AddDays(1);
            var album = new Album { Id = Guid.NewGuid(), Title = title, ArtistId = artist.Id, LibraryId = library.Id, AddedAt = addedAt };
            db.AddRange(album, new Track { Id = Guid.NewGuid(), Title = title + " track", AlbumId = album.Id, LibraryId = library.Id });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return library.Id;
    }

    private static async Task<AlbumPageVM> GetPageAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<AlbumPageVM>(url, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The albums endpoint returned no body.");

    [Fact]
    public async Task Listing_albums_requires_a_signed_in_profile()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/music/albums", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_default_page_is_recently_added_first()
    {
        var libraryId = await SeedLibraryAsync("First", "Second", "Third");
        using var client = Client();

        var page = await GetPageAsync(client, $"/api/music/albums?libraryId={libraryId}");

        page.Total.Should().Be(3);
        page.Offset.Should().Be(0);
        page.Limit.Should().Be(60);
        page.Items.Select(a => a.Title).Should().Equal("Third", "Second", "First");
        page.Items.Should().OnlyContain(a => a.ArtistName == "Seeded Artist");
    }

    [Fact]
    public async Task Sort_offset_and_limit_select_the_requested_page()
    {
        var libraryId = await SeedLibraryAsync("Delta", "Alpha", "Charlie", "Bravo");
        using var client = Client();

        var page = await GetPageAsync(client, $"/api/music/albums?libraryId={libraryId}&sort=Alphabetical&offset=1&limit=2");

        page.Total.Should().Be(4);
        page.Offset.Should().Be(1);
        page.Limit.Should().Be(2);
        page.Items.Select(a => a.Title).Should().Equal("Bravo", "Charlie");
    }

    [Fact]
    public async Task An_oversized_limit_is_capped()
    {
        var libraryId = await SeedLibraryAsync("Only");
        using var client = Client();

        var page = await GetPageAsync(client, $"/api/music/albums?libraryId={libraryId}&limit=5000");

        page.Limit.Should().Be(200);
    }

    [Fact]
    public async Task An_unknown_sort_is_a_bad_request()
    {
        using var client = Client();

        using var response = await client.GetAsync("/api/music/albums?sort=loudest", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
