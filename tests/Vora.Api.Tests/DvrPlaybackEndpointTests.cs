using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Iptv;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public sealed class DvrPlaybackEndpointTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private readonly VoraApiTestFactory _factory;
    private readonly string _recordingPath = Path.Combine(Path.GetTempPath(), "vora-dvr-test-" + Guid.NewGuid().ToString("N") + ".mp4");

    public DvrPlaybackEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
        File.WriteAllBytes(_recordingPath, new byte[] { 0, 0, 0, 0 });
    }

    private async Task<(Guid SessionId, Guid OwnerAccountId)> SeedRecordingAsync()
    {
        var ownerAccountId = Guid.NewGuid();
        var playlist = new IptvPlaylist { Id = Guid.NewGuid(), Name = "Cable" };
        var channel = new IptvChannel { Id = Guid.NewGuid(), ExternalChannelId = "news", Name = "News", StreamUrl = "https://example.test/news", PlaylistId = playlist.Id };
        var schedule = new IptvRecordingSchedule { Id = Guid.NewGuid(), Title = "Evening News", UserId = ownerAccountId, ProfileId = Guid.NewGuid(), ChannelId = channel.Id };
        var session = new IptvRecordingSession
        {
            Id = Guid.NewGuid(),
            Title = "Evening News",
            Status = Vora.Domain.Enums.IptvRecordingSessionStatus.Completed,
            StartTime = DateTime.UtcNow.AddHours(-2),
            EndTime = DateTime.UtcNow.AddHours(-1),
            OutputFilePath = _recordingPath,
            ScheduleId = schedule.Id
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.AddRange(playlist, channel, schedule, session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (session.Id, ownerAccountId);
    }

    private HttpClient ClientFor(Guid accountId, bool isAdmin = false)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(accountId, Guid.NewGuid(), isAdmin));
        return client;
    }

    [Fact]
    public async Task The_account_that_recorded_it_can_play_it()
    {
        var (sessionId, owner) = await SeedRecordingAsync();

        var response = await ClientFor(owner).PostAsync($"/api/streaming/dvr/play/{sessionId}", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_account_cannot_get_a_playback_link_for_it()
    {
        var (sessionId, _) = await SeedRecordingAsync();

        var response = await ClientFor(Guid.NewGuid()).PostAsync($"/api/streaming/dvr/play/{sessionId}", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_admin_can_play_any_recording()
    {
        var (sessionId, _) = await SeedRecordingAsync();

        var response = await ClientFor(Guid.NewGuid(), isAdmin: true).PostAsync($"/api/streaming/dvr/play/{sessionId}", null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public void Dispose()
    {
        if (File.Exists(_recordingPath)) File.Delete(_recordingPath);
    }
}
