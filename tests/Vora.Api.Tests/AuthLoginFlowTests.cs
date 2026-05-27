using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class AuthLoginFlowTests : IClassFixture<VoraApiTestFactory>
{
    private readonly VoraApiTestFactory _factory;

    public AuthLoginFlowTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<User> SeedUserAsync(string email, string password, bool isAdmin = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();

        var user = new User
        {
            Email = email,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsAdmin = isAdmin
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    [Fact]
    public async Task POST_login_returns_access_token_for_valid_credentials()
    {
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var password = "ValidPassword123";
        await SeedUserAsync(email, password);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.DisplayName.Should().Be("Test User");
    }

    [Fact]
    public async Task POST_login_returns_401_for_wrong_password()
    {
        var email = $"test-{Guid.NewGuid():N}@example.com";
        await SeedUserAsync(email, "CorrectPassword");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "WrongPassword" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_login_returns_401_for_unknown_email()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nobody@example.com",
            password = "AnyPassword123"
        },
        TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Issued_access_token_is_accepted_by_protected_endpoints()
    {
        var email = $"test-{Guid.NewGuid():N}@example.com";
        var password = "ValidPassword123";
        await SeedUserAsync(email, password);

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        body.Should().NotBeNull();

        var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        var libraries = await authedClient.GetAsync("/api/libraries", TestContext.Current.CancellationToken);
        libraries.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_response_includes_isAdmin_flag_in_dto()
    {
        // Note: the issued access token itself is account-level and doesn't carry the
        // isAdmin claim — admin authorization runs against the profile-level token
        // returned by /api/auth/exchange-profile-token. The login DTO does expose the
        // flag so the frontend can decide whether to skip the profile picker.
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var password = "AdminPassword123";
        await SeedUserAsync(email, password, isAdmin: true);

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        body.Should().NotBeNull();
        body!.IsAdmin.Should().BeTrue();
    }

    private sealed class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
    }
}
