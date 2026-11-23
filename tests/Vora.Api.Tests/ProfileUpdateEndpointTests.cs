using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class ProfileUpdateEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public ProfileUpdateEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid AccountId, Guid ProfileId)> SeedManagedProfileAsync(bool canRecordLiveTv)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Parent" };
        var profile = new UserProfile
        {
            Name = "Kid",
            UserId = user.Id,
            CanRecordLiveTv = canRecordLiveTv
        };
        db.AddRange(user, profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user.Id, profile.Id);
    }

    private HttpClient Client(Guid accountId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(accountId));
        return client;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid profileId, string json) =>
        client.PutAsync(
            $"/api/users/profiles/{profileId}",
            new StringContent(json, Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    private async Task<bool> CanRecordAsync(Guid profileId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var profile = await db.Set<UserProfile>().AsNoTracking().SingleAsync(p => p.Id == profileId, TestContext.Current.CancellationToken);
        return profile.CanRecordLiveTv;
    }

    [Fact]
    public async Task An_editor_that_does_not_send_the_dvr_permission_leaves_it_on()
    {
        var (accountId, profileId) = await SeedManagedProfileAsync(canRecordLiveTv: true);
        using var client = Client(accountId);

        using var response = await PutAsync(client, profileId, """{"name":"Kid","canAddCustomPodcastFeeds":true}""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CanRecordAsync(profileId)).Should().BeTrue();
    }

    [Fact]
    public async Task An_editor_that_does_not_send_the_dvr_permission_leaves_it_off()
    {
        var (accountId, profileId) = await SeedManagedProfileAsync(canRecordLiveTv: false);
        using var client = Client(accountId);

        using var response = await PutAsync(client, profileId, """{"name":"Kid"}""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CanRecordAsync(profileId)).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_false_revokes_the_dvr_permission()
    {
        var (accountId, profileId) = await SeedManagedProfileAsync(canRecordLiveTv: true);
        using var client = Client(accountId);

        using var response = await PutAsync(client, profileId, """{"name":"Kid","canRecordLiveTv":false}""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CanRecordAsync(profileId)).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_true_grants_the_dvr_permission()
    {
        var (accountId, profileId) = await SeedManagedProfileAsync(canRecordLiveTv: false);
        using var client = Client(accountId);

        using var response = await PutAsync(client, profileId, """{"name":"Kid","canRecordLiveTv":true}""");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CanRecordAsync(profileId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_new_profile_made_without_the_dvr_permission_does_not_get_it()
    {
        var (accountId, _) = await SeedManagedProfileAsync(canRecordLiveTv: false);
        using var client = Client(accountId);

        using var response = await client.PostAsJsonAsync($"/api/users/{accountId}/profiles", new { name = "Guest" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedProfile>(TestContext.Current.CancellationToken);
        (await CanRecordAsync(created!.ProfileId)).Should().BeFalse();
    }

    private sealed record CreatedProfile(Guid ProfileId);
}
