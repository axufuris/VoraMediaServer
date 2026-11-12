using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Iptv.Dtos;
using Vora.Domain.Entities.Iptv;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public sealed class IptvGuideEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public IptvGuideEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private sealed record Seeded(Guid ViewerId, Guid ViewerProfileId, Guid OtherId, Guid OtherProfileId, string ChannelA, string ChannelB);

    private async Task<Seeded> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var a = new IptvPlaylist { Id = Guid.NewGuid(), Name = "A " + suffix, M3uUrl = "https://iptv.example.com/a.m3u" };
        var b = new IptvPlaylist { Id = Guid.NewGuid(), Name = "B " + suffix, M3uUrl = "https://iptv.example.com/b.m3u" };
        var channelA = "guide-a-" + suffix;
        var channelB = "guide-b-" + suffix;
        a.Channels.Add(new IptvChannel { ExternalChannelId = channelA, Name = "A", StreamUrl = "https://iptv.example.com/a.m3u8", PlaylistId = a.Id });
        b.Channels.Add(new IptvChannel { ExternalChannelId = channelB, Name = "B", StreamUrl = "https://iptv.example.com/b.m3u8", PlaylistId = b.Id });

        var viewer = new User { Id = Guid.NewGuid(), Email = $"guide-viewer-{suffix}@example.com", DisplayName = "Viewer", HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { a.Id, b.Id } };
        var viewerProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Viewer", UserId = viewer.Id, HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { a.Id } };
        var other = new User { Id = Guid.NewGuid(), Email = $"guide-other-{suffix}@example.com", DisplayName = "Other", HasAllIptvAccess = false, AllowedIptvPlaylistIds = new List<Guid> { b.Id } };
        var otherProfile = new UserProfile { Id = Guid.NewGuid(), Name = "Other", UserId = other.Id, HasAllIptvAccess = true };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.AddRange(a, b, viewer, viewerProfile, other, otherProfile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new Seeded(viewer.Id, viewerProfile.Id, other.Id, otherProfile.Id, channelA, channelB);
    }

    private async Task<Dictionary<string, List<IptvProgramDto>>> GuideAsync(Guid callerAccount, Guid callerProfile, bool isAdmin, Guid userId, Guid profileId, params string[] channelIds)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(callerAccount, callerProfile, isAdmin));
        var response = await client.PostAsJsonAsync("/api/iptv/guide", new
        {
            userId,
            profileId,
            channelIds,
            startTime = DateTime.UtcNow.AddHours(-1),
            endTime = DateTime.UtcNow.AddHours(3)
        }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<Dictionary<string, List<IptvProgramDto>>>(TestContext.Current.CancellationToken) ?? new();
    }

    [Fact]
    public async Task A_viewer_naming_another_account_gets_the_guide_their_own_profile_allows()
    {
        var seeded = await SeedAsync();

        var guide = await GuideAsync(seeded.ViewerId, seeded.ViewerProfileId, isAdmin: false, seeded.OtherId, seeded.OtherProfileId, seeded.ChannelA, seeded.ChannelB);

        guide.Keys.Should().BeEquivalentTo(seeded.ChannelA);
    }

    [Fact]
    public async Task An_admin_may_still_ask_for_another_account_s_guide()
    {
        var seeded = await SeedAsync();

        var guide = await GuideAsync(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true, seeded.OtherId, seeded.OtherProfileId, seeded.ChannelA, seeded.ChannelB);

        guide.Keys.Should().BeEquivalentTo(seeded.ChannelB);
    }

    [Fact]
    public async Task An_unknown_account_gets_no_guide()
    {
        var seeded = await SeedAsync();

        var guide = await GuideAsync(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true, Guid.NewGuid(), Guid.NewGuid(), seeded.ChannelA, seeded.ChannelB);

        guide.Should().BeEmpty();
    }
}
