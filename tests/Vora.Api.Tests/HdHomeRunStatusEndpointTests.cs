using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Iptv.ViewModels;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class HdHomeRunStatusEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public HdHomeRunStatusEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(bool isAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin));
        return client;
    }

    private async Task<Guid> SeedAsync(IptvPlaylist playlist)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.Add(playlist);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return playlist.Id;
    }

    [Fact]
    public async Task A_non_admin_cannot_read_a_tuner_status()
    {
        var response = await ClientFor(isAdmin: false).GetAsync($"/api/iptv/admin/playlists/{Guid.NewGuid()}/tuners", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_playlist_that_does_not_exist_or_is_not_a_tuner_has_no_status()
    {
        var m3u = await SeedAsync(new IptvPlaylist { Name = "List " + Guid.NewGuid().ToString("N")[..6], M3uUrl = "https://iptv.example.com/list.m3u" });
        var admin = ClientFor(isAdmin: true);

        var missing = await admin.GetAsync($"/api/iptv/admin/playlists/{Guid.NewGuid()}/tuners", TestContext.Current.CancellationToken);
        var notATuner = await admin.GetAsync($"/api/iptv/admin/playlists/{m3u}/tuners", TestContext.Current.CancellationToken);

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        notATuner.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tuner_that_cannot_be_reached_answers_with_the_reason()
    {
        var tuner = await SeedAsync(new IptvPlaylist
        {
            Name = "Antenna " + Guid.NewGuid().ToString("N")[..6],
            SourceType = IptvSourceType.HdHomeRun,
            ServerUrl = "http://127.0.0.1:1"
        });

        var response = await ClientFor(isAdmin: true).GetAsync($"/api/iptv/admin/playlists/{tuner}/tuners", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<HdHomeRunStatusVM>(TestContext.Current.CancellationToken);
        status!.Succeeded.Should().BeFalse();
        status.Error.Should().StartWith("The HDHomeRun could not be reached");
        status.Tuners.Should().BeEmpty();
    }
}
