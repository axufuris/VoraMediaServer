using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vora.Api.Tests.Infra;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Ldap;
using Vora.Application.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class LdapEndpointTests(VoraApiTestFactory factory) : IClassFixture<VoraApiTestFactory>
{
    private const string Slug = "company-directory";
    private const string BindSecret = "service-account-secret-9";
    private const string LocalPassword = "ValidPassword123";

    private readonly FakeLdapDirectory _directory = new();

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<ILdapDirectory>();
        services.AddSingleton<ILdapDirectory>(_directory);
    }));

    private static async Task<T> InScopeAsync<T>(WebApplicationFactory<Program> host, Func<IServiceProvider, VoraDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<VoraDbContext>());
    }

    private static Task SetRegistrationAsync(WebApplicationFactory<Program> host, RegistrationMode mode) =>
        InScopeAsync(host, async (services, _) =>
        {
            var repository = services.GetRequiredService<ISystemSettingsRepository>();
            var settings = await repository.GetSettingsForUpdateAsync();
            settings.RegistrationMode = mode;
            await repository.SaveChangesAsync();
            return 0;
        });

    private static Task<AuthProvider> AddDirectoryAsync(WebApplicationFactory<Program> host, Action<AuthProvider>? change = null) =>
        InScopeAsync(host, async (services, db) =>
        {
            var provider = new AuthProvider
            {
                Type = AuthProviderType.Ldap,
                Slug = Slug,
                DisplayName = "Company directory",
                Enabled = true,
                LdapHost = "ldap.example.org",
                LdapPort = 636,
                LdapSecurity = LdapSecurityMode.Ldaps,
                LdapBindDn = "cn=svc,dc=example,dc=org",
                LdapBindPasswordCiphertext = services.GetRequiredService<IAuthSecretProtector>().Protect(BindSecret),
                LdapUserBaseDn = "ou=people,dc=example,dc=org",
                LdapUserFilter = "(&(objectClass=person)(|(uid={username})(mail={username})))",
                NewUserPolicy = AuthNewUserPolicy.FollowRegistrationMode
            };
            change?.Invoke(provider);
            db.AuthProviders.Add(provider);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return provider;
        });

    private static Task<User> AddLocalUserAsync(WebApplicationFactory<Program> host, string email, string password = LocalPassword, bool isAdmin = false) =>
        InScopeAsync(host, async (_, db) =>
        {
            var user = new User
            {
                Email = email,
                DisplayName = "Local " + email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                HasPassword = true,
                IsAdmin = isAdmin
            };
            user.Profiles.Add(new UserProfile { UserId = user.Id, Name = "Local", User = user, IsAdmin = true });
            db.Users.Add(user);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return user;
        });

    private static HttpClient Anonymous(WebApplicationFactory<Program> host) => host.CreateClient();

    private static HttpClient Admin(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true));
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string name, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email = name, password }, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    // A person the server has not met yet signs in the way the web does: the password login answers 409 with a
    // handle, the sign-in page offers to create the account, and the one-time code is exchanged for the tokens.
    private static async Task<JsonElement> EnrollAsync(HttpClient client, string name, string password)
    {
        using var held = await LoginAsync(client, name, password);
        held.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadAsync(held);
        problem.GetProperty("outcome").GetString().Should().Be("NeedsCreateOrLink");

        using var complete = await client.PostAsJsonAsync("/api/auth/external/complete", new { handle = problem.GetProperty("pending").GetString(), createAccount = true }, TestContext.Current.CancellationToken);
        var completed = await ReadAsync(complete);
        completed.GetProperty("outcome").GetString().Should().Be("SignedIn");
        completed.GetProperty("isNewAccount").GetBoolean().Should().BeTrue();

        using var exchanged = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code = completed.GetProperty("code").GetString() }, TestContext.Current.CancellationToken);
        exchanged.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(exchanged);
    }

    private static async Task<AuthRefreshToken> RefreshOfAsync(WebApplicationFactory<Program> host, Guid userId) =>
        await InScopeAsync(host, (_, db) => db.AuthRefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == userId, TestContext.Current.CancellationToken));

    private static async Task SetPasswordSignInAsync(HttpClient admin, bool enabled)
    {
        using var response = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = enabled }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private FakeLdapDirectory.Account Alice(params string[] groups) =>
        Add(new FakeLdapDirectory.Account("alice", "alice-directory-pw", "alice@example.org", "Alice Example", groups));

    private FakeLdapDirectory.Account Add(FakeLdapDirectory.Account account)
    {
        _directory.Accounts.Add(account);
        return account;
    }

    [Fact]
    public async Task A_new_directory_person_confirms_the_account_once_and_signs_in_straight_through_after_that()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        var provider = await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        using var held = await LoginAsync(client, "alice", "alice-directory-pw");
        var problem = await ReadAsync(held);
        var pending = await ReadAsync(await client.GetAsync($"/api/auth/external/pending/{problem.GetProperty("pending").GetString()}", TestContext.Current.CancellationToken));

        held.StatusCode.Should().Be(HttpStatusCode.Conflict);
        pending.GetProperty("outcome").GetString().Should().Be("NeedsCreateOrLink");
        pending.GetProperty("canCreateAccount").GetBoolean().Should().BeTrue();
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);

        var body = await EnrollAsync(client, "alice", "alice-directory-pw");

        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("isAdmin").GetBoolean().Should().BeFalse();
        var userId = body.GetProperty("userId").GetGuid();
        var user = await InScopeAsync(host, (_, db) => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken));
        user.Email.Should().Be("alice@example.org");
        user.HasPassword.Should().BeFalse();
        var identity = await InScopeAsync(host, (_, db) => db.UserExternalIdentities.AsNoTracking().SingleAsync(i => i.UserId == userId, TestContext.Current.CancellationToken));
        identity.ProviderId.Should().Be(provider.Id);
        identity.Subject.Should().Be("uuid-alice");

        using var next = await LoginAsync(client, "alice", "alice-directory-pw");

        next.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await ReadAsync(next);
        again.GetProperty("userId").GetGuid().Should().Be(userId);
        (await InScopeAsync(host, (_, db) => db.AuthRefreshTokens.AsNoTracking().Where(t => t.UserId == userId).Select(t => t.SignInMethod).ToListAsync(TestContext.Current.CancellationToken)))
            .Should().OnlyContain(method => method == AuthSignInMethod.External);
    }

    [Fact]
    public async Task The_same_person_can_type_their_email_and_lands_in_the_same_account()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        var first = await EnrollAsync(client, "alice", "alice-directory-pw");
        using var second = await LoginAsync(client, "Alice@Example.org", "alice-directory-pw");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(second)).GetProperty("userId").GetGuid().Should().Be(first.GetProperty("userId").GetGuid());
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task A_local_account_signs_in_without_the_directory_ever_being_asked()
    {
        using var host = Host();
        await AddDirectoryAsync(host);
        var local = await AddLocalUserAsync(host, "local@example.com");
        using var client = Anonymous(host);

        using var response = await LoginAsync(client, "local@example.com", LocalPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("userId").GetGuid().Should().Be(local.Id);
        _directory.Calls.Should().Be(0);
        (await RefreshOfAsync(host, local.Id)).SignInMethod.Should().Be(AuthSignInMethod.Password);
    }

    [Fact]
    public async Task With_the_directory_down_a_local_account_still_signs_in_and_a_directory_account_gets_the_bare_refusal()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host);
        Alice();
        await AddLocalUserAsync(host, "boss@example.com", isAdmin: true);
        _directory.Up = false;
        using var client = Anonymous(host);

        using var local = await LoginAsync(client, "boss@example.com", LocalPassword);
        using var directory = await LoginAsync(client, "alice", "alice-directory-pw");

        local.StatusCode.Should().Be(HttpStatusCode.OK);
        directory.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await directory.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("connect").And.NotContain("directory").And.NotContain("example.org");
    }

    [Theory]
    [InlineData("alice", "not-the-password")]
    [InlineData("nobody", "alice-directory-pw")]
    [InlineData("alice", "")]
    public async Task A_wrong_name_or_password_is_the_same_plain_refusal_as_always_with_nothing_about_the_directory(string name, string password)
    {
        using var host = Host();
        await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        using var response = await LoginAsync(client, name, password);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await ReadAsync(response);
        body.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.TryGetProperty("detail", out _).Should().BeFalse();
        body.TryGetProperty("pending", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_directory_entry_without_mail_is_refused_with_a_clear_message_and_no_account_is_made()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host);
        Add(new FakeLdapDirectory.Account("carol", "carol-pw", null, "Carol"));
        using var client = Anonymous(host);

        using var response = await LoginAsync(client, "carol", "carol-pw");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await ReadAsync(response);
        problem.GetProperty("title").GetString().Should().Be("Sign-in refused");
        problem.GetProperty("detail").GetString().Should().Contain("email address");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task A_required_group_keeps_everyone_else_out_and_the_opt_in_admin_group_makes_administrators()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host, p =>
        {
            p.RequiredGroup = "staff";
            p.AdminGroup = "admins";
        });
        Alice("cn=staff,ou=groups,dc=example,dc=org", "cn=admins,ou=groups,dc=example,dc=org");
        Add(new FakeLdapDirectory.Account("bob", "bob-pw", "bob@example.org", "Bob", "cn=staff,ou=groups,dc=example,dc=org"));
        Add(new FakeLdapDirectory.Account("eve", "eve-pw", "eve@example.org", "Eve", "cn=guests,ou=groups,dc=example,dc=org"));
        using var client = Anonymous(host);

        var admin = await EnrollAsync(client, "alice", "alice-directory-pw");
        var member = await EnrollAsync(client, "bob", "bob-pw");
        using var outsider = await LoginAsync(client, "eve", "eve-pw");

        admin.GetProperty("isAdmin").GetBoolean().Should().BeTrue();
        member.GetProperty("isAdmin").GetBoolean().Should().BeFalse();
        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(outsider)).GetProperty("title").GetString().Should().Be("Sign-in refused");
        (await InScopeAsync(host, (_, db) => db.Users.AnyAsync(u => u.Email == "eve@example.org", TestContext.Current.CancellationToken))).Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_group_rule_nobody_becomes_an_administrator_through_the_directory()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host);
        Alice("cn=admins,ou=groups,dc=example,dc=org");
        using var client = Anonymous(host);

        var body = await EnrollAsync(client, "alice", "alice-directory-pw");

        body.GetProperty("isAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task When_registration_is_closed_a_new_directory_person_is_refused_and_existing_ones_are_not()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Disabled);
        var provider = await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        using var closed = await LoginAsync(client, "alice", "alice-directory-pw");
        var handle = (await ReadAsync(closed)).GetProperty("pending").GetString();
        var pending = await ReadAsync(await client.GetAsync($"/api/auth/external/pending/{handle}", TestContext.Current.CancellationToken));
        using var attempt = await client.PostAsJsonAsync("/api/auth/external/complete", new { handle, createAccount = true }, TestContext.Current.CancellationToken);

        closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        pending.GetProperty("canCreateAccount").GetBoolean().Should().BeFalse();
        pending.GetProperty("refusal").GetString().Should().Be("RegistrationClosed");
        (await ReadAsync(attempt)).GetProperty("outcome").GetString().Should().Be("Refused");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);

        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await EnrollAsync(client, "alice", "alice-directory-pw");
        await SetRegistrationAsync(host, RegistrationMode.Disabled);
        using var existing = await LoginAsync(client, "alice", "alice-directory-pw");

        existing.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.ProviderId == provider.Id, TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Fact]
    public async Task A_person_who_matches_a_local_account_by_email_finishes_on_the_sign_in_page_with_the_local_password_once()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        var provider = await AddDirectoryAsync(host);
        var local = await AddLocalUserAsync(host, "sam@example.com");
        Add(new FakeLdapDirectory.Account("sam", "sam-directory-pw", "sam@example.com", "Sam"));
        using var client = Anonymous(host);

        using var held = await LoginAsync(client, "sam", "sam-directory-pw");

        held.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadAsync(held);
        problem.GetProperty("title").GetString().Should().Be("More is needed to finish signing in");
        problem.GetProperty("outcome").GetString().Should().Be("NeedsLinkConfirmation");
        var handle = problem.GetProperty("pending").GetString()!;
        handle.Should().NotBeNullOrWhiteSpace();
        (await held.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("sam-directory-pw");

        var pending = await ReadAsync(await client.GetAsync($"/api/auth/external/pending/{handle}", TestContext.Current.CancellationToken));
        pending.GetProperty("outcome").GetString().Should().Be("NeedsLinkConfirmation");
        pending.GetProperty("providerName").GetString().Should().Be("Company directory");

        using var wrong = await client.PostAsJsonAsync("/api/auth/external/complete", new { handle, linkEmail = "sam@example.com", linkPassword = "wrong-password" }, TestContext.Current.CancellationToken);
        using var right = await client.PostAsJsonAsync("/api/auth/external/complete", new { handle, linkEmail = "sam@example.com", linkPassword = LocalPassword }, TestContext.Current.CancellationToken);

        (await ReadAsync(wrong)).GetProperty("passwordRejected").GetBoolean().Should().BeTrue();
        var completed = await ReadAsync(right);
        completed.GetProperty("outcome").GetString().Should().Be("SignedIn");
        using var exchanged = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code = completed.GetProperty("code").GetString() }, TestContext.Current.CancellationToken);
        (await ReadAsync(exchanged)).GetProperty("userId").GetGuid().Should().Be(local.Id);
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.UserId == local.Id && i.ProviderId == provider.Id, TestContext.Current.CancellationToken))).Should().Be(1);

        using var next = await LoginAsync(client, "sam", "sam-directory-pw");
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(next)).GetProperty("userId").GetGuid().Should().Be(local.Id);
    }

    [Fact]
    public async Task Automatic_linking_signs_in_the_matching_local_account_straight_away_and_never_does_so_when_set_to_never()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host, p => p.LinkByEmail = AuthLinkByEmailPolicy.Automatic);
        var local = await AddLocalUserAsync(host, "sam@example.com");
        Add(new FakeLdapDirectory.Account("sam", "sam-directory-pw", "sam@example.com", "Sam"));
        using var client = Anonymous(host);

        using var response = await LoginAsync(client, "sam", "sam-directory-pw");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("userId").GetGuid().Should().Be(local.Id);

        using var never = Host();
        await SetRegistrationAsync(never, RegistrationMode.Simple);
        await AddDirectoryAsync(never, p => p.LinkByEmail = AuthLinkByEmailPolicy.Never);
        await AddLocalUserAsync(never, "sam@example.com");
        using var neverClient = Anonymous(never);

        using var blocked = await LoginAsync(neverClient, "sam", "sam-directory-pw");

        blocked.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await InScopeAsync(never, (_, db) => db.UserExternalIdentities.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task Five_wrong_attempts_lock_the_name_and_the_directory_is_no_longer_asked()
    {
        using var host = Host();
        await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await LoginAsync(client, "alice", "wrong-" + attempt)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var asked = _directory.Calls;
        using var locked = await LoginAsync(client, "alice", "alice-directory-pw");

        asked.Should().Be(5);
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _directory.Calls.Should().Be(asked);
    }

    [Fact]
    public async Task Sign_in_is_rate_limited_per_address_like_every_other_login()
    {
        using var host = Host();
        await AddDirectoryAsync(host);
        Alice();
        using var client = Anonymous(host);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 14; attempt++)
        {
            using var response = await LoginAsync(client, "name-" + attempt, "wrong");
            statuses.Add(response.StatusCode);
        }

        statuses.Take(10).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task With_password_sign_in_off_directory_people_still_sign_in_and_local_accounts_are_refused_except_administrators()
    {
        using var host = Host();
        await SetRegistrationAsync(host, RegistrationMode.Simple);
        await AddDirectoryAsync(host);
        Alice();
        await AddLocalUserAsync(host, "plain@example.com");
        await AddLocalUserAsync(host, "boss@example.com", isAdmin: true);
        using var admin = Admin(host);
        using var client = Anonymous(host);
        await EnrollAsync(client, "alice", "alice-directory-pw");
        await SetPasswordSignInAsync(admin, false);

        using var directory = await LoginAsync(client, "alice", "alice-directory-pw");
        using var ordinary = await LoginAsync(client, "plain@example.com", LocalPassword);
        using var boss = await LoginAsync(client, "boss@example.com", LocalPassword);
        var providers = await ReadAsync(await client.GetAsync("/api/auth/providers", TestContext.Current.CancellationToken));

        directory.StatusCode.Should().Be(HttpStatusCode.OK);
        ordinary.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ordinary.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Password sign-in is turned off");
        boss.StatusCode.Should().Be(HttpStatusCode.OK);
        providers.GetProperty("passwordSignInEnabled").GetBoolean().Should().BeFalse();
        providers.GetProperty("directorySignInEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Password_sign_in_can_be_turned_off_when_a_directory_is_on_and_not_when_nothing_else_is()
    {
        using var host = Host();
        using var admin = Admin(host);

        using var nothing = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = false }, TestContext.Current.CancellationToken);
        await AddDirectoryAsync(host, p => p.Enabled = false);
        using var disabledDirectory = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = false }, TestContext.Current.CancellationToken);
        await InScopeAsync(host, async (_, db) =>
        {
            (await db.AuthProviders.SingleAsync(TestContext.Current.CancellationToken)).Enabled = true;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
        using var enabledDirectory = await admin.PutAsJsonAsync("/api/admin/auth-settings", new { passwordSignInEnabled = false }, TestContext.Current.CancellationToken);

        nothing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        disabledDirectory.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        enabledDirectory.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_public_list_says_a_directory_is_on_and_never_lists_it_as_a_button_or_shows_its_settings()
    {
        using var host = Host();
        using var client = Anonymous(host);
        var before = await ReadAsync(await client.GetAsync("/api/auth/providers", TestContext.Current.CancellationToken));
        await AddDirectoryAsync(host);

        var raw = await client.GetStringAsync("/api/auth/providers", TestContext.Current.CancellationToken);
        var after = JsonDocument.Parse(raw).RootElement;

        before.GetProperty("directorySignInEnabled").GetBoolean().Should().BeFalse();
        after.GetProperty("directorySignInEnabled").GetBoolean().Should().BeTrue();
        after.GetProperty("providers").GetArrayLength().Should().Be(0);
        raw.Should().NotContain("ldap.example.org").And.NotContain(BindSecret).And.NotContain("cn=svc");
    }

    [Fact]
    public async Task An_admin_saves_a_directory_and_the_bind_password_is_stored_protected_and_never_returned()
    {
        using var host = Host();
        using var admin = Admin(host);

        using var created = await admin.PostAsJsonAsync("/api/admin/auth-providers", DirectoryBody(), TestContext.Current.CancellationToken);

        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        raw.Should().NotContain(BindSecret);
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("type").GetString().Should().Be("Ldap");
        body.GetProperty("hasBindPassword").GetBoolean().Should().BeTrue();
        body.GetProperty("ldapMailAttribute").GetString().Should().Be("mail");
        body.GetProperty("ldapUniqueIdAttribute").GetString().Should().Be("entryUUID");
        body.GetProperty("ldapSecurity").GetString().Should().Be("Ldaps");

        var id = body.GetProperty("id").GetGuid();
        (await admin.GetStringAsync("/api/admin/auth-providers", TestContext.Current.CancellationToken)).Should().NotContain(BindSecret);
        (await admin.GetStringAsync($"/api/admin/auth-providers/{id}", TestContext.Current.CancellationToken)).Should().NotContain(BindSecret);
        var stored = await InScopeAsync(host, (_, db) => db.AuthProviders.AsNoTracking().SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken));
        stored.LdapBindPasswordCiphertext.Should().NotBeNullOrEmpty().And.NotContain(BindSecret);
        host.Services.CreateScope().ServiceProvider.GetRequiredService<IAuthSecretProtector>().Unprotect(stored.LdapBindPasswordCiphertext!).Should().Be(BindSecret);

        using var keep = await admin.PutAsJsonAsync($"/api/admin/auth-providers/{id}", DirectoryBody(bindPassword: null, displayName: "Renamed"), TestContext.Current.CancellationToken);
        (await ReadAsync(keep)).GetProperty("hasBindPassword").GetBoolean().Should().BeTrue();
        using var clear = await admin.PutAsJsonAsync($"/api/admin/auth-providers/{id}", DirectoryBody(bindPassword: null, clearBindPassword: true), TestContext.Current.CancellationToken);
        (await ReadAsync(clear)).GetProperty("hasBindPassword").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("filter", "(uid=fixed)")]
    [InlineData("filter", "(uid={username}")]
    [InlineData("host", "ldap.example.org/path")]
    [InlineData("host", "")]
    [InlineData("port", "70000")]
    [InlineData("base", "")]
    [InlineData("mail", "mail)(uid=*")]
    [InlineData("security", "None")]
    [InlineData("groupFilter", "(member=fixed)")]
    public async Task An_invalid_directory_is_a_bad_request_and_nothing_is_saved(string field, string value)
    {
        using var host = Host();
        using var admin = Admin(host);
        var body = DirectoryBody(
            filter: field == "filter" ? value : "(&(objectClass=person)(uid={username}))",
            host: field == "host" ? value : "ldap.example.org",
            port: field == "port" ? int.Parse(value) : 636,
            baseDn: field == "base" ? value : "ou=people,dc=example,dc=org",
            mail: field == "mail" ? value : "mail",
            security: field == "security" ? value : "Ldaps",
            groupFilter: field == "groupFilter" ? value : null);

        using var response = await admin.PostAsJsonAsync("/api/admin/auth-providers", body, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await InScopeAsync(host, (_, db) => db.AuthProviders.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task Plain_ldap_is_allowed_only_to_a_host_on_a_private_network()
    {
        using var host = Host();
        using var admin = Admin(host);

        using var lan = await admin.PostAsJsonAsync("/api/admin/auth-providers", DirectoryBody(host: "192.168.1.20", port: 389, security: "None"), TestContext.Current.CancellationToken);
        using var internet = await admin.PostAsJsonAsync("/api/admin/auth-providers", DirectoryBody(slug: "other", host: "ldap.example.org", port: 389, security: "None"), TestContext.Current.CancellationToken);

        lan.StatusCode.Should().Be(HttpStatusCode.OK);
        internet.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_test_button_reports_what_the_directory_answered_and_what_to_fix()
    {
        using var host = Host();
        var provider = await AddDirectoryAsync(host);
        using var admin = Admin(host);
        _directory.Sample = new LdapSampleResult(true, true, 5, false, 4, 1, 5, ["uid=a,ou=people,dc=example,dc=org", "uid=b,ou=people,dc=example,dc=org"], null);

        using var good = await admin.PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);
        _directory.Sample = new LdapSampleResult(true, false, 0, false, 0, 0, 0, [], "The directory refused the bind account.");
        using var refused = await admin.PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);
        using var missing = await admin.PostAsync($"/api/admin/auth-providers/{Guid.NewGuid()}/test", null, TestContext.Current.CancellationToken);
        using var anonymous = await Anonymous(host).PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);

        var ok = await ReadAsync(good);
        ok.GetProperty("ok").GetBoolean().Should().BeTrue();
        ok.GetProperty("connected").GetBoolean().Should().BeTrue();
        ok.GetProperty("serviceBound").GetBoolean().Should().BeTrue();
        ok.GetProperty("entriesFound").GetInt32().Should().Be(5);
        ok.GetProperty("entriesWithMail").GetInt32().Should().Be(4);
        ok.GetProperty("entriesWithoutMail").GetInt32().Should().Be(1);
        ok.GetProperty("sampleDns").GetArrayLength().Should().Be(2);
        ok.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain(w => w!.Contains("mail attribute"));
        var bad = await ReadAsync(refused);
        bad.GetProperty("ok").GetBoolean().Should().BeFalse();
        bad.GetProperty("message").GetString().Should().Be("The directory refused the bind account.");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _directory.LastSettings!.BindPassword.Should().Be(BindSecret);
    }

    [Fact]
    public async Task A_directory_that_is_turned_off_is_not_asked_to_sign_anyone_in()
    {
        using var host = Host();
        await AddDirectoryAsync(host, p => p.Enabled = false);
        Alice();
        using var client = Anonymous(host);

        using var response = await LoginAsync(client, "alice", "alice-directory-pw");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _directory.Calls.Should().Be(0);
    }

    private static object DirectoryBody(
        string slug = Slug,
        string displayName = "Company directory",
        string host = "ldap.example.org",
        int port = 636,
        string security = "Ldaps",
        string? bindPassword = BindSecret,
        bool clearBindPassword = false,
        string filter = "(&(objectClass=person)(uid={username}))",
        string baseDn = "ou=people,dc=example,dc=org",
        string mail = "mail",
        string? groupFilter = null) => new
        {
            type = "Ldap",
            slug,
            displayName,
            enabled = true,
            ldapHost = host,
            ldapPort = port,
            ldapSecurity = security,
            ldapBindDn = "cn=svc,dc=example,dc=org",
            ldapBindPassword = bindPassword,
            clearLdapBindPassword = clearBindPassword,
            ldapUserBaseDn = baseDn,
            ldapUserFilter = filter,
            ldapMailAttribute = mail,
            ldapGroupBaseDn = groupFilter is null ? null : "ou=groups,dc=example,dc=org",
            ldapGroupFilter = groupFilter
        };
}
