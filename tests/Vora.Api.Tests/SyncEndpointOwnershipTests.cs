using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Api.Tests.Infra;
using Vora.Application.Sync;
using Vora.Application.Sync.ViewModels;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public sealed class SyncEndpointOwnershipTests : IClassFixture<VoraApiTestFactory>, IDisposable
{
    private readonly ISyncAndStateManager _manager = Substitute.For<ISyncAndStateManager>();
    private readonly WebApplicationFactory<Program> _host;

    public SyncEndpointOwnershipTests(VoraApiTestFactory factory)
    {
        _host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISyncAndStateManager>();
            services.AddSingleton(_manager);
        }));
        _manager.GetContinueWatchingFeedAsync(Arg.Any<Guid>(), Arg.Any<int>())
            .Returns(Enumerable.Empty<ContinueWatchingVM>());
    }

    public void Dispose() => _host.Dispose();

    private async Task<(Guid AccountId, Guid ProfileId)> SeedAccountAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Andy" };
        var profile = new UserProfile { Name = "Andy", UserId = user.Id };
        db.AddRange(user, profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user.Id, profile.Id);
    }

    private HttpClient SignedIn(Guid accountId, Guid profileId, bool isAdmin = false)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestHelpers.IssueProfileToken(accountId, profileId, isAdmin));
        return client;
    }

    private static Task<HttpResponseMessage> ContinueWatchingAsync(HttpClient client, Guid profileId) =>
        client.GetAsync($"/api/sync/profiles/{profileId}/continue-watching", TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> HideAsync(HttpClient client, Guid profileId, Guid mediaItemId) =>
        client.PutAsync($"/api/sync/profiles/{profileId}/continue-watching/{mediaItemId}/hide", null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Another_accounts_continue_watching_is_forbidden()
    {
        var (_, victimProfile) = await SeedAccountAsync();
        var (callerAccount, callerProfile) = await SeedAccountAsync();
        using var client = SignedIn(callerAccount, callerProfile);

        using var response = await ContinueWatchingAsync(client, victimProfile);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _manager.DidNotReceiveWithAnyArgs().GetContinueWatchingFeedAsync(default, default);
    }

    [Fact]
    public async Task Hiding_a_title_in_another_accounts_continue_watching_is_forbidden()
    {
        var (_, victimProfile) = await SeedAccountAsync();
        var (callerAccount, callerProfile) = await SeedAccountAsync();
        using var client = SignedIn(callerAccount, callerProfile);

        using var response = await HideAsync(client, victimProfile, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await _manager.DidNotReceiveWithAnyArgs().HideFromContinueWatchingAsync(default, default);
    }

    [Fact]
    public async Task A_profile_id_that_belongs_to_no_account_is_forbidden()
    {
        var (callerAccount, callerProfile) = await SeedAccountAsync();
        using var client = SignedIn(callerAccount, callerProfile);

        using var response = await ContinueWatchingAsync(client, Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_account_reads_its_own_profiles_continue_watching()
    {
        var (accountId, profileId) = await SeedAccountAsync();
        using var client = SignedIn(accountId, profileId);

        using var response = await ContinueWatchingAsync(client, profileId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _manager.Received(1).GetContinueWatchingFeedAsync(profileId, Arg.Any<int>());
    }

    [Fact]
    public async Task An_account_hides_a_title_in_its_own_profiles_continue_watching()
    {
        var (accountId, profileId) = await SeedAccountAsync();
        var mediaItemId = Guid.NewGuid();
        using var client = SignedIn(accountId, profileId);

        using var response = await HideAsync(client, profileId, mediaItemId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _manager.Received(1).HideFromContinueWatchingAsync(profileId, mediaItemId);
    }

    [Fact]
    public async Task An_admin_reads_another_accounts_continue_watching()
    {
        var (_, otherProfile) = await SeedAccountAsync();
        var (adminAccount, adminProfile) = await SeedAccountAsync();
        using var client = SignedIn(adminAccount, adminProfile, isAdmin: true);

        using var response = await ContinueWatchingAsync(client, otherProfile);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _manager.Received(1).GetContinueWatchingFeedAsync(otherProfile, Arg.Any<int>());
    }

    [Fact]
    public async Task An_admin_hides_a_title_in_another_accounts_continue_watching()
    {
        var (_, otherProfile) = await SeedAccountAsync();
        var (adminAccount, adminProfile) = await SeedAccountAsync();
        var mediaItemId = Guid.NewGuid();
        using var client = SignedIn(adminAccount, adminProfile, isAdmin: true);

        using var response = await HideAsync(client, otherProfile, mediaItemId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _manager.Received(1).HideFromContinueWatchingAsync(otherProfile, mediaItemId);
    }
}
