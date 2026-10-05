using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Backups;
using Vora.Application.Backups.ViewModels;

namespace Vora.Api.Tests;

public class BackupSectionsTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public BackupSectionsTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private List<string> RegisteredKeys()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetServices<IBackupSection>().Select(s => s.Key).ToList();
    }

    private HttpClient Client(bool isAdmin)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: isAdmin));
        return client;
    }

    [Fact]
    public void Every_section_has_a_unique_key()
    {
        var keys = RegisteredKeys();

        keys.Should().OnlyHaveUniqueItems();
        keys.Should().Contain(new[]
        {
            "settings.webhooks", "library.collections", "users.playlists", "users.watchlists",
            "users.stations", "users.requests", "podcasts.shows", "podcasts.listening"
        });
    }

    [Theory]
    [InlineData("users.profiles", "users.devices")]
    [InlineData("users.profiles", "iptv.recording-schedules")]
    [InlineData("users.profiles", "users.watch-history")]
    [InlineData("users.profiles", "users.playlists")]
    [InlineData("users.profiles", "podcasts.listening")]
    [InlineData("users.devices", "users.watch-history")]
    [InlineData("library.collections", "library.smart-lists")]
    [InlineData("iptv.playlists", "iptv.tuner-profiles")]
    [InlineData("iptv.playlists", "users.channel-favorites")]
    [InlineData("discovery.request-servers", "users.requests")]
    [InlineData("podcasts.shows", "podcasts.listening")]
    public void Sections_restore_after_the_sections_their_rows_point_at(string first, string then)
    {
        var keys = RegisteredKeys();

        keys.IndexOf(first).Should().BeGreaterThanOrEqualTo(0);
        keys.IndexOf(first).Should().BeLessThan(keys.IndexOf(then));
    }

    [Fact]
    public async Task Size_estimate_covers_every_section_for_an_admin()
    {
        var response = await Client(isAdmin: true).GetAsync("/api/admin/backups/sections/estimate?refresh=true", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var estimate = await response.Content.ReadFromJsonAsync<BackupSizeEstimateVM>(TestContext.Current.CancellationToken);
        estimate.Should().NotBeNull();
        var sections = estimate?.Sections ?? new List<BackupSectionEstimateVM>();
        sections.Select(s => s.Key).Should().BeEquivalentTo(RegisteredKeys());
        sections.Should().OnlyContain(s => !s.Failed);
        sections.Where(s => s.Key != "settings.data-protection").Should().OnlyContain(s => s.EstimatedBytes > 0);
    }

    [Fact]
    public async Task Size_estimate_is_admin_only()
    {
        var response = await Client(isAdmin: false).GetAsync("/api/admin/backups/sections/estimate", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
