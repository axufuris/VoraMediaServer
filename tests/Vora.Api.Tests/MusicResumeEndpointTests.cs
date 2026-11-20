using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Media;
using Vora.Application.Media.ViewModels;
using Vora.Domain.Entities.Library;
using Vora.Domain.Entities.Media;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class MusicResumeEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly VoraApiTestFactory _factory;
    private readonly Guid _account = Guid.NewGuid();
    private readonly Guid _profile = Guid.NewGuid();

    public MusicResumeEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(Guid? profile = null)
    {
        var client = _factory.CreateClient();
        var token = JwtTestHelpers.IssueProfileToken(_account, profile ?? _profile);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<List<Track>> SeedSongsAsync(params string[] titles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var library = new MediaLibrary { Id = Guid.NewGuid(), Name = "Songs", Type = LibraryType.Music, FolderPaths = new List<string> { "/music" } };
        var artist = new Artist { Id = Guid.NewGuid(), Name = "Seeded Artist", LibraryId = library.Id };
        var album = new Album { Id = Guid.NewGuid(), Title = "Seeded Album", ArtistId = artist.Id, LibraryId = library.Id };
        var tracks = titles.Select((title, i) => new Track { Id = Guid.NewGuid(), Title = title, TrackNumber = i + 1, AlbumId = album.Id, LibraryId = library.Id }).ToList();
        db.AddRange(library, artist, album);
        db.AddRange(tracks);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tracks;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync("/api/music/resume", body, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Every_resume_call_needs_a_signed_in_profile()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync("/api/music/resume", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/music/resume/queue?source=None", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PutAsync(client, new { source = "None", trackIds = new[] { Guid.NewGuid() }, index = 0 })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_saved_queue_comes_back_as_a_tile_and_as_the_queue_to_play()
    {
        var songs = await SeedSongsAsync("One", "Two", "Three");
        using var client = Client();

        using var saved = await PutAsync(client, new { source = "None", trackIds = songs.Select(s => s.Id), index = 1 });
        saved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tiles = await client.GetFromJsonAsync<List<MusicResumeItemVM>>("/api/music/resume", Json, TestContext.Current.CancellationToken);
        var tile = tiles.Should().ContainSingle().Subject;
        tile.Kind.Should().Be(RecentlyPlayedKind.Track);
        tile.Title.Should().Be("Two");
        tile.Source.Should().Be(MusicPlaySource.None);
        tile.SourceId.Should().BeNull();
        tile.TrackIndex.Should().Be(1);

        var queue = await client.GetFromJsonAsync<MusicResumeQueueVM>("/api/music/resume/queue?source=None", Json, TestContext.Current.CancellationToken);
        queue!.Tracks.Select(t => t.Title).Should().Equal("One", "Two", "Three");
        queue.Index.Should().Be(1);
    }

    [Fact]
    public async Task Saving_again_replaces_the_point_and_an_empty_queue_clears_it()
    {
        var songs = await SeedSongsAsync("One", "Two");
        var album = Guid.NewGuid();
        using var client = Client(Guid.NewGuid());

        (await PutAsync(client, new { source = "Album", sourceId = album, trackIds = songs.Select(s => s.Id), index = 0 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PutAsync(client, new { source = "Album", sourceId = album, trackIds = songs.Select(s => s.Id), index = 1 })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<MusicResumeItemVM>>("/api/music/resume", Json, TestContext.Current.CancellationToken)).Should().ContainSingle().Which.TrackIndex.Should().Be(1);

        (await PutAsync(client, new { source = "Album", sourceId = album, trackIds = Array.Empty<Guid>(), index = 0 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<List<MusicResumeItemVM>>("/api/music/resume", Json, TestContext.Current.CancellationToken)).Should().BeEmpty();
        using var gone = await client.GetAsync($"/api/music/resume/queue?source=Album&sourceId={album}", TestContext.Current.CancellationToken);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_save_that_cannot_be_kept_is_a_bad_request()
    {
        var songs = await SeedSongsAsync("One");
        using var client = Client(Guid.NewGuid());

        (await PutAsync(client, new { source = "Playlist", trackIds = songs.Select(s => s.Id), index = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(client, new { source = "None", trackIds = songs.Select(s => s.Id), index = 5 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(client, new { trackIds = songs.Select(s => s.Id), index = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PutAsync(client, new { source = "None", index = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_profile_only_sees_its_own_points()
    {
        var songs = await SeedSongsAsync("One");
        using var mine = Client(Guid.NewGuid());
        using var theirs = Client(Guid.NewGuid());
        await PutAsync(mine, new { source = "None", trackIds = songs.Select(s => s.Id), index = 0 });

        (await theirs.GetFromJsonAsync<List<MusicResumeItemVM>>("/api/music/resume", Json, TestContext.Current.CancellationToken)).Should().BeEmpty();
        using var none = await theirs.GetAsync("/api/music/resume/queue?source=None", TestContext.Current.CancellationToken);
        none.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
