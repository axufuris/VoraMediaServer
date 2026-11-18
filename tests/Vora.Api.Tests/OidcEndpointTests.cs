using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vora.Api.Tests.Infra;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Oidc;
using Vora.Application.Settings;
using Vora.Application.Tests.Auth.Oidc;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Tests;

public class OidcEndpointTests(VoraApiTestFactory factory) : IClassFixture<VoraApiTestFactory>
{
    private const string Slug = "authentik";

    private readonly MockOidcProvider _idp = new();

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        services.AddHttpClient(OidcProviderClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _idp)));

    private static HttpClient Browser(WebApplicationFactory<Program> host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

    private static Task<AuthProvider> AddProviderAsync(WebApplicationFactory<Program> host, Action<AuthProvider>? change = null) =>
        InScopeAsync(host, async (services, db) =>
        {
            var provider = new AuthProvider
            {
                Slug = Slug,
                DisplayName = "Authentik",
                Enabled = true,
                Issuer = MockOidcProvider.Issuer,
                ClientId = MockOidcProvider.ClientId,
                ClientSecretCiphertext = services.GetRequiredService<IAuthSecretProtector>().Protect("the-secret"),
                NewUserPolicy = AuthNewUserPolicy.IdpDecides
            };
            change?.Invoke(provider);
            db.AuthProviders.Add(provider);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return provider;
        });

    private static Task<User> AddUserAsync(WebApplicationFactory<Program> host, string email = "sam@example.com", string? password = "ValidPassword123", bool isAdmin = false) =>
        InScopeAsync(host, async (_, db) =>
        {
            var user = new User
            {
                Email = email,
                DisplayName = "Sam",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password ?? Guid.NewGuid().ToString("N")),
                HasPassword = password is not null,
                IsAdmin = isAdmin
            };
            user.Profiles.Add(new UserProfile { UserId = user.Id, Name = "Sam", User = user, IsAdmin = true });
            db.Users.Add(user);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return user;
        });

    private static Task LinkAsync(WebApplicationFactory<Program> host, Guid userId, Guid providerId, string subject = "subject-1") =>
        InScopeAsync(host, async (_, db) =>
        {
            db.UserExternalIdentities.Add(new UserExternalIdentity { UserId = userId, ProviderId = providerId, Subject = subject, Email = "sam@example.com" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });

    private sealed record Callback(Uri Location, Dictionary<string, string?> Query, string State, string Nonce, string Challenge);

    private static Dictionary<string, string?> ParseQuery(Uri uri) =>
        QueryHelpers.ParseQuery(uri.Query).ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    private async Task<(string State, string Nonce, string Challenge, string RedirectUri)> StartAsync(HttpClient browser, string query = "")
    {
        using var response = await browser.GetAsync($"/api/auth/oidc/{Slug}/start{query}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var parameters = ParseQuery(response.Headers.Location!);
        return (parameters["state"]!, parameters["nonce"]!, parameters["code_challenge"]!, parameters["redirect_uri"]!);
    }

    private async Task<Callback> SignInAsync(HttpClient browser, Action<JsonObject>? change = null, string query = "", Func<string, string>? nonce = null)
    {
        var (state, startNonce, challenge, _) = await StartAsync(browser, query);
        _idp.NextIdToken = _idp.IssueIdToken(nonce is null ? startNonce : nonce(startNonce), change);
        return await CallbackAsync(browser, state, startNonce, challenge);
    }

    private async Task<Callback> CallbackAsync(HttpClient browser, string state, string nonce = "", string challenge = "", string? error = null)
    {
        var url = $"/api/auth/oidc/{Slug}/callback?state={Uri.EscapeDataString(state)}" + (error is null ? "&code=the-code" : $"&error={error}");
        using var response = await browser.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return new Callback(response.Headers.Location!, ParseQuery(response.Headers.Location!), state, nonce, challenge);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.Clone();

    private static async Task<JsonElement> ExchangeAsync(HttpClient client, string code)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/external/exchange", new { code }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> PendingAsync(HttpClient client, string handle)
    {
        using var response = await client.GetAsync($"/api/auth/external/pending/{handle}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> CompleteAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/external/complete", body, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ReadAsync(response);
    }

    [Fact]
    public async Task Starting_sends_the_browser_to_the_provider_with_PKCE_and_the_exact_redirect_address()
    {
        using var host = Host();
        await AddProviderAsync(host);

        var (state, nonce, challenge, redirectUri) = await StartAsync(Browser(host), "?returnTo=/movies");

        redirectUri.Should().Be($"http://localhost/api/auth/oidc/{Slug}/callback");
        state.Length.Should().BeGreaterThan(30);
        nonce.Length.Should().BeGreaterThan(10);
        challenge.Length.Should().Be(43);
    }

    [Fact]
    public async Task A_linked_account_signs_in_and_the_one_time_code_gives_the_same_tokens_as_a_login()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        await LinkAsync(host, user.Id, provider.Id);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, query: "?returnTo=/movies/1");

        callback.Location.GetLeftPart(UriPartial.Path).Should().Be("http://localhost/login/external");
        callback.Query["returnTo"].Should().Be("/movies/1");
        var tokens = await ExchangeAsync(browser, callback.Query["code"]!);
        tokens.GetProperty("userId").GetGuid().Should().Be(user.Id);
        tokens.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        tokens.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        tokens.GetProperty("isAdmin").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_token_exchange_with_the_provider_carries_the_PKCE_verifier_that_hashes_to_the_challenge()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        await LinkAsync(host, user.Id, provider.Id);
        using var browser = Browser(host);
        var (state, nonce, challenge, redirectUri) = await StartAsync(browser);
        _idp.NextIdToken = _idp.IssueIdToken(nonce);

        await CallbackAsync(browser, state, nonce, challenge);

        var form = _idp.LastTokenForm();
        form["redirect_uri"].Should().Be(redirectUri);
        form["code"].Should().Be("the-code");
        Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))).Should().Be(challenge);
        _idp.LastTokenRequest!.Authorization.Should().StartWith("Basic ");
    }

    [Fact]
    public async Task A_new_person_chooses_to_create_an_account_and_it_is_an_ordinary_one_that_does_not_claim_the_server()
    {
        using var host = Host();
        await AddProviderAsync(host);
        await SetRegistrationAsync(host, RegistrationMode.Disabled);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser);

        var handle = callback.Query["pending"]!;
        var pending = await PendingAsync(browser, handle);
        pending.GetProperty("outcome").GetString().Should().Be("NeedsCreateOrLink");
        pending.GetProperty("canCreateAccount").GetBoolean().Should().BeTrue();
        pending.GetProperty("providerName").GetString().Should().Be("Authentik");
        var completed = await CompleteAsync(browser, new { handle, createAccount = true });
        completed.GetProperty("outcome").GetString().Should().Be("SignedIn");
        completed.GetProperty("isNewAccount").GetBoolean().Should().BeTrue();
        var tokens = await ExchangeAsync(browser, completed.GetProperty("code").GetString()!);
        tokens.GetProperty("isAdmin").GetBoolean().Should().BeFalse();

        var user = await InScopeAsync(host, (_, db) => db.Users.Include(u => u.Profiles).Include(u => u.ExternalIdentities).AsNoTracking().SingleAsync(u => u.Email == "sam@example.com", TestContext.Current.CancellationToken));
        user.IsAdmin.Should().BeFalse();
        user.HasPassword.Should().BeFalse();
        user.DisplayName.Should().Be("Sam");
        user.Profiles.Should().ContainSingle();
        user.ExternalIdentities.Should().ContainSingle().Which.Subject.Should().Be("subject-1");
        using var status = await browser.GetAsync("/api/auth/setup-status", TestContext.Current.CancellationToken);
        (await ReadAsync(status)).GetProperty("isClaimed").GetBoolean().Should().BeFalse();
        (await PendingResponseAsync(browser, handle)).Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<HttpStatusCode> PendingResponseAsync(HttpClient client, string handle)
    {
        using var response = await client.GetAsync($"/api/auth/external/pending/{handle}", TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task Following_the_registration_mode_a_closed_server_offers_no_account_to_create()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.NewUserPolicy = AuthNewUserPolicy.FollowRegistrationMode);
        await SetRegistrationAsync(host, RegistrationMode.Disabled);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser);
        var pending = await PendingAsync(browser, callback.Query["pending"]!);
        var attempt = await CompleteAsync(browser, new { handle = callback.Query["pending"], createAccount = true });

        pending.GetProperty("canCreateAccount").GetBoolean().Should().BeFalse();
        attempt.GetProperty("outcome").GetString().Should().Be("Refused");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task In_invite_PIN_mode_the_PIN_is_asked_for_and_used_up_by_the_account()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.NewUserPolicy = AuthNewUserPolicy.FollowRegistrationMode);
        await SetRegistrationAsync(host, RegistrationMode.SecretWord);
        await InScopeAsync(host, async (_, db) =>
        {
            db.RegistrationTickets.Add(new RegistrationTicket { SecretCode = "4821", ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
        using var browser = Browser(host);
        var handle = (await SignInAsync(browser)).Query["pending"]!;

        var noPin = await CompleteAsync(browser, new { handle, createAccount = true });
        var wrong = await CompleteAsync(browser, new { handle, createAccount = true, invitePin = "0000" });
        var right = await CompleteAsync(browser, new { handle, createAccount = true, invitePin = "4821" });

        noPin.GetProperty("outcome").GetString().Should().Be("NeedsInvitePin");
        wrong.GetProperty("invitePinRejected").GetBoolean().Should().BeTrue();
        right.GetProperty("outcome").GetString().Should().Be("SignedIn");
        (await InScopeAsync(host, (_, db) => db.RegistrationTickets.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task In_invitation_mode_an_invitation_for_the_verified_email_makes_the_account_without_a_choice()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.NewUserPolicy = AuthNewUserPolicy.FollowRegistrationMode);
        await SetRegistrationAsync(host, RegistrationMode.Invitation);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("invite-token"))).ToLowerInvariant();
        await InScopeAsync(host, async (_, db) =>
        {
            db.InvitationTickets.Add(new InvitationTicket { Email = "sam@example.com", TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddDays(3) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, query: "?invite=invite-token");

        callback.Query.Should().ContainKey("code");
        var tokens = await ExchangeAsync(browser, callback.Query["code"]!);
        tokens.GetProperty("isAdmin").GetBoolean().Should().BeFalse();
        (await InScopeAsync(host, (_, db) => db.InvitationTickets.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task An_invitation_for_another_address_does_not_let_this_person_in()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.NewUserPolicy = AuthNewUserPolicy.FollowRegistrationMode);
        await SetRegistrationAsync(host, RegistrationMode.Invitation);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("invite-token"))).ToLowerInvariant();
        await InScopeAsync(host, async (_, db) =>
        {
            db.InvitationTickets.Add(new InvitationTicket { Email = "someone-else@example.com", TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddDays(3) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return 0;
        });
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, query: "?invite=invite-token");
        var pending = await PendingAsync(browser, callback.Query["pending"]!);

        pending.GetProperty("canCreateAccount").GetBoolean().Should().BeFalse();
        pending.GetProperty("refusal").GetString().Should().Be("InvitationMismatch");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
        (await InScopeAsync(host, (_, db) => db.InvitationTickets.CountAsync(TestContext.Current.CancellationToken))).Should().Be(1);
    }

    [Theory]
    [InlineData("vora-admins", "vora-admins", true)]
    [InlineData("vora-admins", "vora-users", false)]
    [InlineData(null, "vora-admins", false)]
    public async Task Only_the_admin_group_the_administrator_set_can_make_an_administrator(string? adminGroup, string tokenGroup, bool expectedAdmin)
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.AdminGroup = adminGroup);
        using var browser = Browser(host);
        var handle = (await SignInAsync(browser, payload => payload["groups"] = new JsonArray(tokenGroup))).Query["pending"]!;

        var completed = await CompleteAsync(browser, new { handle, createAccount = true });

        (await ExchangeAsync(browser, completed.GetProperty("code").GetString()!)).GetProperty("isAdmin").GetBoolean().Should().Be(expectedAdmin);
        (await InScopeAsync(host, (_, db) => db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken))).IsAdmin.Should().Be(expectedAdmin);
    }

    [Fact]
    public async Task A_person_without_the_required_group_is_refused_and_nothing_is_created()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.RequiredGroup = "vora-users");
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, payload => payload["groups"] = new JsonArray("other"));
        var pending = await PendingAsync(browser, callback.Query["pending"]!);

        pending.GetProperty("outcome").GetString().Should().Be("Refused");
        pending.GetProperty("refusal").GetString().Should().Be("GroupNotAllowed");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task A_local_account_with_the_same_verified_email_links_only_after_its_password_is_given_once()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        using var browser = Browser(host);
        var handle = (await SignInAsync(browser)).Query["pending"]!;

        var pending = await PendingAsync(browser, handle);
        var wrong = await CompleteAsync(browser, new { handle, linkEmail = "sam@example.com", linkPassword = "wrong-password" });
        var right = await CompleteAsync(browser, new { handle, linkEmail = "sam@example.com", linkPassword = "ValidPassword123" });

        pending.GetProperty("outcome").GetString().Should().Be("NeedsLinkConfirmation");
        wrong.GetProperty("outcome").GetString().Should().Be("NeedsLinkConfirmation");
        wrong.GetProperty("passwordRejected").GetBoolean().Should().BeTrue();
        right.GetProperty("outcome").GetString().Should().Be("SignedIn");
        (await ExchangeAsync(browser, right.GetProperty("code").GetString()!)).GetProperty("userId").GetGuid().Should().Be(user.Id);
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.UserId == user.Id && i.ProviderId == provider.Id, TestContext.Current.CancellationToken))).Should().Be(1);

        var next = await SignInAsync(browser);
        next.Query.Should().ContainKey("code").And.NotContainKey("pending");
    }

    [Fact]
    public async Task An_unverified_email_never_links_to_the_account_that_uses_it_even_with_automatic_linking()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.LinkByEmail = AuthLinkByEmailPolicy.Automatic);
        var user = await AddUserAsync(host);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, payload => payload["email_verified"] = false);
        var pending = await PendingAsync(browser, callback.Query["pending"]!);

        callback.Query.Should().NotContainKey("code");
        pending.GetProperty("outcome").GetString().Should().Be("Refused");
        pending.GetProperty("refusal").GetString().Should().Be("EmailNotVerified");
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.UserId == user.Id, TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task An_account_an_admin_added_without_a_password_links_on_the_first_verified_sign_in()
    {
        using var host = Host();
        await AddProviderAsync(host);
        var user = await AddUserAsync(host, password: null);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser);

        var tokens = await ExchangeAsync(browser, callback.Query["code"]!);
        tokens.GetProperty("userId").GetGuid().Should().Be(user.Id);
    }

    [Fact]
    public async Task A_state_works_once_so_a_replayed_callback_leads_nowhere()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        await LinkAsync(host, user.Id, provider.Id);
        using var browser = Browser(host);
        var first = await SignInAsync(browser);

        var replay = await CallbackAsync(browser, first.State);

        first.Query.Should().ContainKey("code");
        replay.Query["error"].Should().Be("state_expired");
        replay.Query.Should().NotContainKey("code");
    }

    [Fact]
    public async Task One_time_codes_from_a_sign_in_work_once()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        await LinkAsync(host, user.Id, provider.Id);
        using var browser = Browser(host);
        var code = (await SignInAsync(browser)).Query["code"]!;

        await ExchangeAsync(browser, code);

        using var replay = await browser.PostAsJsonAsync("/api/auth/external/exchange", new { code }, TestContext.Current.CancellationToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("access_denied", "access_denied")]
    [InlineData("server_error", "provider_error")]
    public async Task An_error_from_the_provider_comes_back_as_a_short_code_on_the_login_page(string error, string expected)
    {
        using var host = Host();
        await AddProviderAsync(host);
        using var browser = Browser(host);
        var (state, _, _, _) = await StartAsync(browser);

        var callback = await CallbackAsync(browser, state, error: error);

        callback.Location.GetLeftPart(UriPartial.Path).Should().Be("http://localhost/login/external");
        callback.Query["error"].Should().Be(expected);
    }

    public static TheoryData<string> BadTokens => new() { "nonce", "audience", "issuer", "expired", "signature" };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task A_token_that_fails_validation_signs_nobody_in_and_creates_nothing(string fault)
    {
        using var host = Host();
        await AddProviderAsync(host);
        using var browser = Browser(host);
        var (state, nonce, challenge, _) = await StartAsync(browser);
        _idp.NextIdToken = fault switch
        {
            "nonce" => _idp.IssueIdToken("someone-elses-nonce"),
            "audience" => _idp.IssueIdToken(nonce, payload => payload["aud"] = "another-client"),
            "issuer" => _idp.IssueIdToken(nonce, payload => payload["iss"] = "https://evil.example.com"),
            "expired" => _idp.IssueIdToken(nonce, expires: DateTime.UtcNow.AddHours(-2)),
            _ => _idp.IssueIdToken(nonce, signWith: MockOidcProvider.AnotherKey())
        };

        var callback = await CallbackAsync(browser, state, nonce, challenge);

        callback.Query["error"].Should().Be("id_token_invalid");
        callback.Query.Should().NotContainKey("code").And.NotContainKey("pending");
        (await InScopeAsync(host, (_, db) => db.Users.CountAsync(TestContext.Current.CancellationToken))).Should().Be(0);
    }

    [Fact]
    public async Task A_provider_that_is_off_or_unknown_sends_the_browser_back_with_a_reason()
    {
        using var host = Host();
        await AddProviderAsync(host, p => p.Enabled = false);
        using var browser = Browser(host);

        using var off = await browser.GetAsync($"/api/auth/oidc/{Slug}/start", TestContext.Current.CancellationToken);
        using var unknown = await browser.GetAsync("/api/auth/oidc/nothing/start", TestContext.Current.CancellationToken);

        ParseQuery(off.Headers.Location!)["error"].Should().Be("provider_unavailable");
        ParseQuery(unknown.Headers.Location!)["error"].Should().Be("provider_unavailable");
        off.Headers.Location!.GetLeftPart(UriPartial.Path).Should().Be("http://localhost/login/external");
    }

    [Fact]
    public async Task A_provider_that_answers_with_a_redirect_is_not_followed()
    {
        using var host = Host();
        await AddProviderAsync(host);
        _idp.DiscoveryStatus = HttpStatusCode.Found;
        using var browser = Browser(host);

        using var response = await browser.GetAsync($"/api/auth/oidc/{Slug}/start", TestContext.Current.CancellationToken);

        ParseQuery(response.Headers.Location!)["error"].Should().Be("discovery_failed");
        _idp.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("?returnTo=https://evil.example.com/steal")]
    [InlineData("?returnTo=//evil.example.com")]
    [InlineData("?returnTo=javascript:alert(1)")]
    public async Task A_return_address_outside_this_server_is_dropped(string query)
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var user = await AddUserAsync(host);
        await LinkAsync(host, user.Id, provider.Id);
        using var browser = Browser(host);

        var callback = await SignInAsync(browser, query: query);

        callback.Query.Should().ContainKey("code").And.NotContainKey("returnTo");
        callback.Location.Host.Should().Be("localhost");
    }

    [Fact]
    public async Task A_signed_in_person_links_a_provider_from_account_settings_and_cannot_take_one_that_is_someone_elses()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        var owner = await AddUserAsync(host, "owner@example.com");
        var other = await AddUserAsync(host, "other@example.com");
        using var browser = Browser(host);
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(owner.Id));

        using var start = await browser.PostAsJsonAsync($"/api/auth/oidc/{Slug}/link", new { returnTo = "/account" }, TestContext.Current.CancellationToken);
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = (await ReadAsync(start)).GetProperty("url").GetString()!;
        var parameters = ParseQuery(new Uri(url));
        _idp.NextIdToken = _idp.IssueIdToken(parameters["nonce"]!, payload => payload["sub"] = "linked-subject");
        var linked = await CallbackAsync(browser, parameters["state"]!);

        linked.Location.AbsolutePath.Should().Be("/account");
        linked.Query["linked"].Should().Be(Slug);
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.UserId == owner.Id && i.Subject == "linked-subject", TestContext.Current.CancellationToken))).Should().Be(1);
        var listed = JsonDocument.Parse(await browser.GetStringAsync("/api/auth/identities", TestContext.Current.CancellationToken)).RootElement;
        listed.GetArrayLength().Should().Be(1);

        using var otherBrowser = Browser(host);
        otherBrowser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueAccountToken(other.Id));
        using var second = await otherBrowser.PostAsJsonAsync($"/api/auth/oidc/{Slug}/link", new { }, TestContext.Current.CancellationToken);
        var secondParameters = ParseQuery(new Uri((await ReadAsync(second)).GetProperty("url").GetString()!));
        _idp.NextIdToken = _idp.IssueIdToken(secondParameters["nonce"]!, payload => payload["sub"] = "linked-subject");
        var conflict = await CallbackAsync(otherBrowser, secondParameters["state"]!);

        conflict.Location.AbsolutePath.Should().Be("/account");
        conflict.Query["linkError"].Should().Be("link_conflict");
        (await InScopeAsync(host, (_, db) => db.UserExternalIdentities.CountAsync(i => i.UserId == other.Id, TestContext.Current.CancellationToken))).Should().Be(0);
        provider.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Linking_needs_a_signed_in_account()
    {
        using var host = Host();
        await AddProviderAsync(host);
        using var browser = Browser(host);

        using var response = await browser.PostAsJsonAsync($"/api/auth/oidc/{Slug}/link", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_connection_test_is_for_admins_and_reports_what_it_found()
    {
        using var host = Host();
        var provider = await AddProviderAsync(host);
        using var ordinary = host.CreateClient();
        ordinary.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false));
        using var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestHelpers.IssueProfileToken(Guid.NewGuid(), Guid.NewGuid(), isAdmin: true));

        using var refused = await ordinary.PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);
        using var good = await admin.PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);
        _idp.DiscoveryIssuer = "https://other.example.com";
        using var mismatch = await admin.PostAsync($"/api/admin/auth-providers/{provider.Id}/test", null, TestContext.Current.CancellationToken);
        using var missing = await admin.PostAsync($"/api/admin/auth-providers/{Guid.NewGuid()}/test", null, TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var ok = await ReadAsync(good);
        ok.GetProperty("ok").GetBoolean().Should().BeTrue();
        ok.GetProperty("signingKeyCount").GetInt32().Should().Be(1);
        ok.GetProperty("tokenEndpoint").GetString().Should().EndWith("/token");
        (await ReadAsync(mismatch)).GetProperty("ok").GetBoolean().Should().BeFalse();
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_start_and_callback_endpoints_are_rate_limited_per_address()
    {
        using var host = Host();
        await AddProviderAsync(host);
        using var browser = Browser(host);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 45; attempt++)
        {
            using var response = await browser.GetAsync($"/api/auth/oidc/{Slug}/start", TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
        statuses.Take(10).Should().OnlyContain(status => status == HttpStatusCode.Redirect);
    }
}
