using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Iptv.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public sealed class IptvClientPlaylistsEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public IptvClientPlaylistsEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private sealed record Seeded(Guid ViewerId, Guid ViewerProfileId, Guid OtherId, Guid OtherProfileId, Guid PaidId, Guid FreeId);

    private async Task<Seeded> SeedAsync()
    {
        var paid = new IptvPlaylist
        {
            Id = Guid.NewGuid(),
            Name = "Paid " + Guid.NewGuid().ToString("N")[..6],
            SourceType = IptvSourceType.Xtream,
            ServerUrl = "http://provider.example.com:8080",
            Username = "viewer",
            PasswordCiphertext = "sealed",
            LastError = "The provider answered HTTP 502."
        };
        paid.Channels.Add(new IptvChannel { ExternalChannelId = "news.uk", Name = "News", StreamUrl = "xtream:1001.m3u8", PlaylistId = paid.Id });
        var free = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Free " + Guid.NewGuid().ToString("N")[..6], M3uUrl = "https://iptv.example.com/free.m3u" };
        free.Channels.Add(new IptvChannel { ExternalChannelId = "free", Name = "Free", StreamUrl = "https://iptv.example.com/free.m3u8", PlaylistId = free.Id });

        var viewer = new User { Id = Guid.NewGuid(), Email = $"viewer-{Guid.NewGuid():N}@example.com", DisplayName = "Viewer", HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { paid.Id, free.Id } };
        var viewerProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Viewer", UserId = viewer.Id, HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { free.Id } };
        var other = new User { Id = Guid.NewGuid(), Email = $"other-{Guid.NewGuid():N}@example.com", DisplayName = "Other", HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { paid.Id } };
        var otherProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Other", UserId = other.Id, HasAllIptvAccess = true };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.AddRange(paid, free, viewer, viewerProfile, other, otherProfile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seeded(viewer.Id, viewerProfile.Id, other.Id, otherProfile.Id, paid.Id, free.Id);
    }

    private HttpClient ClientFor(Guid accountId, Guid profileId, bool isAdmin = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(accountId, profileId, isAdmin));
        return client;
    }

    private static async Task<List<IptvPlaylistVM>> PlaylistsAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<List<IptvPlaylistVM>>(TestContext.Current.CancellationToken) ?? new List<IptvPlaylistVM>();
    }

    [Fact]
    public async Task A_viewer_asking_about_another_account_gets_their_own_playlists()
    {
        var seeded = await SeedAsync();

        var response = await ClientFor(seeded.ViewerId, seeded.ViewerProfileId).GetAsync($"/api/iptv/client/playlists/{seeded.OtherId}?profileId={seeded.OtherProfileId}", TestContext.Current.CancellationToken);

        var playlists = await PlaylistsAsync(response);
        playlists.Select(p => p.Id).Should().Equal(seeded.FreeId);
    }

    [Fact]
    public async Task The_viewer_s_profile_allowlist_applies_even_when_the_request_names_no_profile()
    {
        var seeded = await SeedAsync();

        var response = await ClientFor(seeded.ViewerId, seeded.ViewerProfileId).GetAsync($"/api/iptv/client/playlists/{seeded.ViewerId}", TestContext.Current.CancellationToken);

        (await PlaylistsAsync(response)).Select(p => p.Id).Should().Equal(seeded.FreeId);
    }

    [Fact]
    public async Task An_admin_may_ask_about_another_account_and_still_gets_no_secrets()
    {
        var seeded = await SeedAsync();

        var response = await ClientFor(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true).GetAsync($"/api/iptv/client/playlists/{seeded.OtherId}?profileId={seeded.OtherProfileId}", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paid = (await PlaylistsAsync(response)).Single();
        paid.Id.Should().Be(seeded.PaidId);
        paid.M3uUrl.Should().Be("http://provider.example.com");
        paid.LastError.Should().BeNull();
        paid.Channels.Single().StreamUrl.Should().StartWith("vora-channel:");
        body.Should().NotContain("xtream:").And.NotContain("sealed").And.NotContain(":8080").And.NotContain("\"viewer\"");
    }
}
