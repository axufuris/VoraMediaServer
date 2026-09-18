using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class ProfilePinGateTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public ProfilePinGateTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid AccountId, Guid ProfileId)> SeedAsync(string? pin)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User { Email = $"{Guid.NewGuid():N}@example.com", DisplayName = "Andy" };
        var profile = new UserProfile
        {
            Name = "Andy",
            UserId = user.Id,
            PinHash = pin == null ? null : BCrypt.Net.BCrypt.HashPassword(pin)
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

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient client, Guid accountId, Guid profileId, string? pin) =>
        client.PostAsJsonAsync($"/api/auth/exchange-profile-token?accountId={accountId}&profileId={profileId}", new { pin }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_pin_protected_profile_is_refused_without_the_pin()
    {
        var (accountId, profileId) = await SeedAsync("1234");
        using var client = Client(accountId);

        using var response = await ExchangeAsync(client, accountId, profileId, null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wrong_pin_is_refused_without_looking_like_an_expired_session()
    {
        var (accountId, profileId) = await SeedAsync("1234");
        using var client = Client(accountId);

        using var response = await ExchangeAsync(client, accountId, profileId, "9999");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_right_pin_yields_a_profile_token()
    {
        var (accountId, profileId) = await SeedAsync("1234");
        using var client = Client(accountId);

        using var response = await ExchangeAsync(client, accountId, profileId, "1234");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenBody>(TestContext.Current.CancellationToken);
        body!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_profile_without_a_pin_still_needs_no_pin()
    {
        var (accountId, profileId) = await SeedAsync(null);
        using var client = Client(accountId);

        using var response = await ExchangeAsync(client, accountId, profileId, null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_wrong_pin_on_validate_pin_is_not_a_401()
    {
        var (accountId, profileId) = await SeedAsync("1234");
        using var client = Client(accountId);

        using var response = await client.PostAsJsonAsync($"/api/users/profiles/{profileId}/validate-pin", new { pin = "9999" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed class TokenBody
    {
        public string? Token { get; set; }
    }
}
