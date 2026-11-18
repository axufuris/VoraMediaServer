using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Auth.External;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class ExternalAuthEndpointTests : IClassFixture<VoraApiTestFactory>
{
    private const string Secret = "client-secret-value-123";

    private readonly VoraApiTestFactory _factory;

    public ExternalAuthEndpointTests(VoraApiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(bool? isAdmin)
    {
        var client = _factory.CreateClient();
        if (isAdmin is not null)
        {
            var token = JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: isAdmin.Value);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private HttpClient AccountClient(Guid accountId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(accountId));
        return client;
    }

    private static string NewSlug() => "idp-" + Guid.NewGuid().ToString("N")[..8];

    private static object ProviderBody(string slug, bool enabled = true, string? secret = Secret) => new
    {
        slug,
        displayName = "Authentik",
        enabled,
        issuer = "https://auth.example.com/application/o/vora/",
        clientId = "vora-client",
        clientSecret = secret
    };

    private async Task<JsonElement> CreateProviderAsync(string slug, bool enabled = true)
    {
        using var admin = Client(isAdmin: true);
        using var response = await admin.PostAsJsonAsync("/api/admin/auth-providers", ProviderBody(slug, enabled), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();
    }

    private async Task<User> SeedUserAsync(string email, string password = "ValidPassword123", bool isAdmin = false, bool hasPassword = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var user = new User
        {
            Email = email,
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            HasPassword = hasPassword,
            IsAdmin = isAdmin
        };
        user.Profiles.Add(new UserProfile { UserId = user.Id, Name = "Test User", User = user, IsAdmin = true });
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private async Task SetPasswordSignInAsync(bool enabled)
    {
        using var admin = Client(isAdmin: true);
        using var response = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = enabled }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_admins_manage_sign_in_providers_and_settings()
    {
        var body = ProviderBody(NewSlug());
        using var anonymous = Client(isAdmin: null);
        using var ordinary = Client(isAdmin: false);

        foreach (var client in new[] { anonymous, ordinary })
        {
            var list = await client.GetAsync("/api/admin/auth-providers", TestContext.Current.CancellationToken);
            var create = await client.PostAsJsonAsync("/api/admin/auth-providers", body, TestContext.Current.CancellationToken);
            var settings = await client.GetAsync("/api/admin/auth-settings", TestContext.Current.CancellationToken);
            var update = await client.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = true }, TestContext.Current.CancellationToken);

            var expected = ReferenceEquals(client, anonymous) ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            new[] { list, create, settings, update }.Select(r => r.StatusCode).Should().AllBeEquivalentTo(expected);
        }
    }

    [Fact]
    public async Task A_provider_is_saved_without_its_secret_ever_coming_back_and_the_secret_is_stored_protected()
    {
        var slug = NewSlug();

        var created = await CreateProviderAsync(slug);

        created.GetProperty("hasSecret").GetBoolean().Should().BeTrue();
        created.GetProperty("redirectUri").GetString().Should().EndWith($"/api/auth/oidc/{slug}/callback");
        using var admin = Client(isAdmin: true);
        var list = await admin.GetStringAsync("/api/admin/auth-providers", TestContext.Current.CancellationToken);
        var one = await admin.GetStringAsync($"/api/admin/auth-providers/{created.GetProperty("id").GetGuid()}", TestContext.Current.CancellationToken);
        list.Should().NotContain(Secret);
        one.Should().NotContain(Secret);

        using var scope = _factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<VoraDbContext>().AuthProviders.AsNoTracking().SingleAsync(p => p.Slug == slug, TestContext.Current.CancellationToken);
        stored.ClientSecretCiphertext.Should().NotBeNullOrEmpty().And.NotContain(Secret);
        scope.ServiceProvider.GetRequiredService<IAuthSecretProtector>().Unprotect(stored.ClientSecretCiphertext!).Should().Be(Secret);
    }

    [Fact]
    public async Task Updating_without_a_secret_keeps_it_and_a_taken_slug_is_a_conflict()
    {
        var slug = NewSlug();
        var created = await CreateProviderAsync(slug);
        var id = created.GetProperty("id").GetGuid();
        using var admin = Client(isAdmin: true);

        using var keep = await admin.PutAsJsonAsync($"/api/admin/auth-providers/{id}", ProviderBody(slug, secret: null), TestContext.Current.CancellationToken);
        using var duplicate = await admin.PostAsJsonAsync("/api/admin/auth-providers", ProviderBody(slug), TestContext.Current.CancellationToken);
        using var invalid = await admin.PostAsJsonAsync("/api/admin/auth-providers", ProviderBody("Bad Slug!"), TestContext.Current.CancellationToken);

        keep.StatusCode.Should().Be(HttpStatusCode.OK);
        (await keep.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("hasSecret").GetBoolean().Should().BeTrue();
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_public_list_shows_only_turned_on_providers_and_no_configuration()
    {
        var on = NewSlug();
        var off = NewSlug();
        await CreateProviderAsync(on);
        await CreateProviderAsync(off, enabled: false);

        using var anonymous = Client(isAdmin: null);
        var raw = await anonymous.GetStringAsync("/api/auth/providers", TestContext.Current.CancellationToken);
        var json = JsonDocument.Parse(raw).RootElement;

        var slugs = json.GetProperty("providers").EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ToList();
        slugs.Should().Contain(on).And.NotContain(off);
        json.GetProperty("passwordSignInEnabled").GetBoolean().Should().BeTrue();
        json.GetProperty("registrationMode").ValueKind.Should().Be(JsonValueKind.String);
        raw.Should().NotContain(Secret).And.NotContain("clientId").And.NotContain("issuer");
    }

    [Fact]
    public async Task A_one_time_code_is_exchanged_for_the_same_tokens_a_login_gives_and_works_only_once()
    {
        var user = await SeedUserAsync($"sso-{Guid.NewGuid():N}@example.com");
        var code = _factory.Services.GetRequiredService<IExternalSignInCodeStore>().Issue(user.Id, Guid.NewGuid());
        using var client = Client(isAdmin: null);

        using var first = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code }, TestContext.Current.CancellationToken);
        using var replay = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code }, TestContext.Current.CancellationToken);
        using var guess = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code = "made-up" }, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("userId").GetGuid().Should().Be(user.Id);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        guess.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var refresh = await scope.ServiceProvider.GetRequiredService<VoraDbContext>().AuthRefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id, TestContext.Current.CancellationToken);
        refresh.SignInMethod.Should().Be(AuthSignInMethod.External);
    }

    [Fact]
    public async Task A_code_for_an_account_that_was_deleted_is_refused()
    {
        var code = _factory.Services.GetRequiredService<IExternalSignInCodeStore>().Issue(Guid.NewGuid(), Guid.NewGuid());
        using var client = Client(isAdmin: null);

        using var response = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Turning_off_password_sign_in_refuses_ordinary_accounts_but_not_administrators_and_registration_too()
    {
        await CreateProviderAsync(NewSlug());
        var ordinary = $"plain-{Guid.NewGuid():N}@example.com";
        var admin = $"boss-{Guid.NewGuid():N}@example.com";
        await SeedUserAsync(ordinary);
        await SeedUserAsync(admin, isAdmin: true);
        await SetPasswordSignInAsync(false);
        try
        {
            using var client = Client(isAdmin: null);

            using var ordinaryLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = ordinary, password = "ValidPassword123" }, TestContext.Current.CancellationToken);
            using var adminLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = admin, password = "ValidPassword123" }, TestContext.Current.CancellationToken);
            using var register = await client.PostAsJsonAsync("/api/auth/register", new { email = $"new-{Guid.NewGuid():N}@example.com", password = "ValidPassword123", displayName = "New" }, TestContext.Current.CancellationToken);
            var providers = JsonDocument.Parse(await client.GetStringAsync("/api/auth/providers", TestContext.Current.CancellationToken)).RootElement;

            ordinaryLogin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ordinaryLogin.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Password sign-in is turned off");
            adminLogin.StatusCode.Should().Be(HttpStatusCode.OK);
            register.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            providers.GetProperty("passwordSignInEnabled").GetBoolean().Should().BeFalse();
        }
        finally
        {
            await SetPasswordSignInAsync(true);
        }
    }

    [Fact]
    public async Task Password_sign_in_cannot_be_turned_off_while_no_provider_is_on()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        db.AuthProviders.RemoveRange(db.AuthProviders);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var admin = Client(isAdmin: true);
        using var response = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = false }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_account_an_admin_added_without_a_password_cannot_sign_in_with_one()
    {
        var email = $"sso-only-{Guid.NewGuid():N}@example.com";
        using var admin = Client(isAdmin: true);

        using var created = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "Sso Only", ssoOnly = true }, TestContext.Current.CancellationToken);
        using var client = Client(isAdmin: null);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "AnythingAtAll1" }, TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.OK);
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<VoraDbContext>().Users.AsNoTracking().SingleAsync(u => u.Email == email, TestContext.Current.CancellationToken)).HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task Someone_lists_and_unlinks_their_own_sign_ins_but_never_their_last_way_in()
    {
        var provider = await CreateProviderAsync(NewSlug());
        var providerId = provider.GetProperty("id").GetGuid();
        var passwordless = await SeedUserAsync($"only-{Guid.NewGuid():N}@example.com", hasPassword: false);
        var withPassword = await SeedUserAsync($"both-{Guid.NewGuid():N}@example.com");
        var lonely = await LinkAsync(passwordless.Id, providerId);
        var spare = await LinkAsync(withPassword.Id, providerId);

        using var lonelyClient = AccountClient(passwordless.Id);
        using var spareClient = AccountClient(withPassword.Id);
        var listed = JsonDocument.Parse(await lonelyClient.GetStringAsync("/api/auth/identities", TestContext.Current.CancellationToken)).RootElement;
        using var refused = await lonelyClient.DeleteAsync($"/api/auth/identities/{lonely}", TestContext.Current.CancellationToken);
        using var someoneElses = await spareClient.DeleteAsync($"/api/auth/identities/{lonely}", TestContext.Current.CancellationToken);
        using var allowed = await spareClient.DeleteAsync($"/api/auth/identities/{spare}", TestContext.Current.CancellationToken);

        listed.GetArrayLength().Should().Be(1);
        listed[0].GetProperty("providerName").GetString().Should().Be("Authentik");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        someoneElses.StatusCode.Should().Be(HttpStatusCode.NotFound);
        allowed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Linked_sign_ins_need_a_signed_in_account()
    {
        using var anonymous = Client(isAdmin: null);

        (await anonymous.GetAsync("/api/auth/identities", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"/api/auth/identities/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Removing_a_provider_that_would_lock_accounts_out_needs_force()
    {
        var provider = await CreateProviderAsync(NewSlug());
        var providerId = provider.GetProperty("id").GetGuid();
        var user = await SeedUserAsync($"stranded-{Guid.NewGuid():N}@example.com", hasPassword: false);
        await LinkAsync(user.Id, providerId);
        using var admin = Client(isAdmin: true);

        using var refused = await admin.DeleteAsync($"/api/admin/auth-providers/{providerId}", TestContext.Current.CancellationToken);
        using var forced = await admin.DeleteAsync($"/api/admin/auth-providers/{providerId}?force=true", TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        forced.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<Guid> LinkAsync(Guid userId, Guid providerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
        var identity = new UserExternalIdentity { UserId = userId, ProviderId = providerId, Subject = Guid.NewGuid().ToString(), Email = "x@example.com" };
        db.UserExternalIdentities.Add(identity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return identity.Id;
    }
}
