using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Oidc;
using Vora.Application.Auth.ViewModels;
using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth.Oidc;

public class OidcSignInManagerTests
{
    private const string Origin = "https://vora.example.com";

    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IOidcProviderClient _client = Substitute.For<IOidcProviderClient>();
    private readonly IOidcIdTokenValidator _validator = Substitute.For<IOidcIdTokenValidator>();
    private readonly IExternalSignInService _signIn = Substitute.For<IExternalSignInService>();
    private readonly ManualClock _clock = new();
    private readonly OidcLoginStateStore _states;
    private readonly ExternalPendingStore _pendings;
    private readonly ExternalSignInCodeStore _codes;
    private readonly OidcSignInManager _manager;
    private readonly AuthProvider _provider = OidcTestKit.Provider();
    private readonly OidcDiscovery _discovery = new("https://idp.example.com/realms/vora", "https://idp.example.com/realms/vora/auth?ui=1", "https://idp.example.com/realms/vora/token", "https://idp.example.com/realms/vora/jwks", null, true, true, false);
    private readonly Guid _userId = Guid.NewGuid();

    public OidcSignInManagerTests()
    {
        _states = new OidcLoginStateStore(_clock);
        _pendings = new ExternalPendingStore(_clock);
        _codes = new ExternalSignInCodeStore(_clock);
        _manager = new OidcSignInManager(_providers, _settings, _client, _validator, _states, _pendings, _codes, _signIn, NullLogger<OidcSignInManager>.Instance);

        _settings.GetSettingsAsync().Returns(new ServerSetting());
        _providers.GetBySlugAsync("authentik").Returns(_provider);
        _client.GetDiscoveryAsync(_provider, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_discovery);
        _client.ExchangeCodeAsync(_provider, _discovery, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new OidcTokenResponse("id-token", "access-token"));
        _validator.ValidateAsync(_provider, _discovery, "id-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object> { ["sub"] = "subject-1", ["email"] = "sam@example.com", ["email_verified"] = true, ["name"] = "Sam" });
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
    }

    private static Dictionary<string, string> Query(string url)
    {
        var query = url[(url.IndexOf('?', url.IndexOf('?') + 1) + 1)..];
        return query.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
    }

    private async Task<(string State, Dictionary<string, string> Parameters)> StartAsync(OidcStartRequest? request = null)
    {
        var result = await _manager.StartAsync("authentik", request ?? new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(OidcStartOutcome.Redirect);
        var parameters = Query(result.RedirectUrl!);
        return (parameters["state"], parameters);
    }

    private Task<OidcCallbackResult> CompleteAsync(string? state, string? code = "the-code", string? error = null) =>
        _manager.CompleteAsync("authentik", new OidcCallbackParameters(code, state, error), Origin, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Starting_sends_the_person_to_the_provider_with_PKCE_a_state_and_a_nonce()
    {
        var (state, parameters) = await StartAsync();

        parameters["response_type"].Should().Be("code");
        parameters["client_id"].Should().Be(MockOidcProvider.ClientId);
        parameters["redirect_uri"].Should().Be("https://vora.example.com/api/auth/oidc/authentik/callback");
        parameters["scope"].Should().Be("openid email profile");
        parameters["code_challenge_method"].Should().Be("S256");
        parameters["nonce"].Should().NotBeNullOrWhiteSpace();
        state.Length.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task The_challenge_is_the_hash_of_the_verifier_the_server_keeps_for_the_token_exchange()
    {
        var (state, parameters) = await StartAsync();

        await CompleteAsync(state);

        var verifier = (string)_client.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IOidcProviderClient.ExchangeCodeAsync)).GetArguments()[4]!;
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).Should().Be(parameters["code_challenge"]);
        verifier.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    [Fact]
    public async Task The_authorization_address_may_already_carry_a_query()
    {
        var result = await _manager.StartAsync("authentik", new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken);

        result.RedirectUrl.Should().StartWith("https://idp.example.com/realms/vora/auth?ui=1&response_type=code");
    }

    [Fact]
    public async Task The_redirect_address_is_the_one_the_provider_was_told_even_if_the_public_address_changes_later()
    {
        var (state, _) = await StartAsync();
        _settings.GetSettingsAsync().Returns(new ServerSetting { PublicBaseUrl = "https://changed.example.com" });

        await CompleteAsync(state);

        await _client.Received(1).ExchangeCodeAsync(_provider, _discovery, "the-code", "https://vora.example.com/api/auth/oidc/authentik/callback", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_public_address_setting_decides_the_redirect_address()
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { PublicBaseUrl = "https://vora.example.net" });

        var (_, parameters) = await StartAsync();

        parameters["redirect_uri"].Should().Be("https://vora.example.net/api/auth/oidc/authentik/callback");
    }

    [Theory]
    [InlineData("unknown")]
    public async Task A_provider_that_does_not_exist_cannot_be_started(string slug)
    {
        var result = await _manager.StartAsync(slug, new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(OidcStartOutcome.Failed);
        result.ErrorCode.Should().Be(OidcErrorCodes.ProviderUnavailable);
    }

    [Fact]
    public async Task A_provider_that_is_off_or_not_filled_in_cannot_be_started()
    {
        _provider.Enabled = false;
        (await _manager.StartAsync("authentik", new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken)).ErrorCode.Should().Be(OidcErrorCodes.ProviderUnavailable);

        _provider.Enabled = true;
        _provider.ClientId = null;
        (await _manager.StartAsync("authentik", new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken)).ErrorCode.Should().Be(OidcErrorCodes.ProviderUnavailable);
    }

    [Fact]
    public async Task A_provider_that_cannot_be_reached_reports_why_without_starting()
    {
        _client.GetDiscoveryAsync(_provider, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns<OidcDiscovery>(_ => throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, "offline"));

        var result = await _manager.StartAsync("authentik", new OidcStartRequest(null, null, null), Origin, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(OidcStartOutcome.Failed);
        result.ErrorCode.Should().Be(OidcErrorCodes.DiscoveryFailed);
    }

    [Theory]
    [InlineData("/movies/123", "/movies/123")]
    [InlineData("/a?b=c", "/a?b=c")]
    [InlineData("https://evil.example.com/", null)]
    [InlineData("//evil.example.com", null)]
    [InlineData("/\\evil.example.com", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("movies", null)]
    [InlineData("", null)]
    [InlineData("/with\ncontrol", null)]
    public void Only_a_path_on_this_server_may_be_returned_to(string value, string? expected)
    {
        OidcSignInManager.SafeReturnTo(value).Should().Be(expected);
    }

    [Fact]
    public void A_very_long_return_path_is_dropped()
    {
        OidcSignInManager.SafeReturnTo("/" + new string('a', 600)).Should().BeNull();
    }

    [Fact]
    public async Task A_signed_in_person_gets_a_one_time_code_and_the_page_they_came_from()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
        var (state, _) = await StartAsync(new OidcStartRequest("/music", null, null));

        var result = await CompleteAsync(state);

        result.Outcome.Should().Be(OidcCallbackOutcome.SignedIn);
        result.ReturnTo.Should().Be("/music");
        _codes.Redeem(result.Code!).Should().Be(new ExternalSignInGrant(_userId, _provider.Id));
        OidcRedirects.Destination(result).Should().Be($"https://vora.example.com/login/external?code={Uri.EscapeDataString(result.Code!)}&returnTo=%2Fmusic");
    }

    [Fact]
    public async Task The_claims_the_provider_vouched_for_and_the_invite_token_reach_the_rules()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
        var (state, _) = await StartAsync(new OidcStartRequest(null, "  invite-token  ", null));

        await CompleteAsync(state);

        await _signIn.Received(1).SignInAsync(
            _provider.Id,
            Arg.Is<ExternalClaims>(c => c.Subject == "subject-1" && c.Email == "sam@example.com" && c.EmailVerified && c.DisplayName == "Sam"),
            Arg.Is<ExternalSignInOptions>(o => o.InviteToken == "invite-token" && !o.CreateAccount && o.LinkTo == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_state_works_once_so_a_replayed_callback_is_refused()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
        var (state, _) = await StartAsync();

        var first = await CompleteAsync(state);
        var replay = await CompleteAsync(state);

        first.Outcome.Should().Be(OidcCallbackOutcome.SignedIn);
        replay.Outcome.Should().Be(OidcCallbackOutcome.Failed);
        replay.ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
    }

    [Fact]
    public async Task A_guessed_missing_or_expired_state_is_refused_before_anything_is_exchanged()
    {
        var (state, _) = await StartAsync();
        _clock.Now = _clock.Now.AddMinutes(11);

        (await CompleteAsync("guess")).ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
        (await CompleteAsync(null)).ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
        (await CompleteAsync(state)).ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
        await _client.DidNotReceive().ExchangeCodeAsync(Arg.Any<AuthProvider>(), Arg.Any<OidcDiscovery>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_state_started_for_one_provider_does_not_complete_another()
    {
        var (state, _) = await StartAsync();
        var other = OidcTestKit.Provider(p => { p.Slug = "keycloak"; });
        _providers.GetBySlugAsync("keycloak").Returns(other);

        var result = await _manager.CompleteAsync("keycloak", new OidcCallbackParameters("code", state, null), Origin, TestContext.Current.CancellationToken);

        result.ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
    }

    [Theory]
    [InlineData("access_denied", OidcErrorCodes.AccessDenied)]
    [InlineData("server_error", OidcErrorCodes.ProviderError)]
    public async Task An_error_from_the_provider_ends_the_sign_in_and_uses_up_the_state(string error, string expected)
    {
        var (state, _) = await StartAsync();

        var result = await CompleteAsync(state, code: null, error: error);
        var again = await CompleteAsync(state);

        result.ErrorCode.Should().Be(expected);
        OidcRedirects.Destination(result).Should().Be($"https://vora.example.com/login/external?error={expected}");
        again.ErrorCode.Should().Be(OidcErrorCodes.StateExpired);
        await _client.DidNotReceive().ExchangeCodeAsync(Arg.Any<AuthProvider>(), Arg.Any<OidcDiscovery>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_callback_without_a_code_fails()
    {
        var (state, _) = await StartAsync();

        (await CompleteAsync(state, code: null)).ErrorCode.Should().Be(OidcErrorCodes.ProviderError);
    }

    [Theory]
    [InlineData(OidcErrorCodes.TokenExchangeFailed)]
    [InlineData(OidcErrorCodes.DiscoveryFailed)]
    public async Task A_failed_exchange_ends_the_sign_in_with_its_error_code(string code)
    {
        _client.ExchangeCodeAsync(_provider, _discovery, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<OidcTokenResponse>(_ => throw new OidcProviderException(code, "failed"));
        var (state, _) = await StartAsync();

        var result = await CompleteAsync(state);

        result.Outcome.Should().Be(OidcCallbackOutcome.Failed);
        result.ErrorCode.Should().Be(code);
        await _signIn.DidNotReceive().SignInAsync(Arg.Any<Guid>(), Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_token_that_fails_validation_signs_nobody_in()
    {
        _validator.ValidateAsync(_provider, _discovery, "id-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, object>>(_ => throw new OidcProviderException(OidcErrorCodes.IdTokenInvalid, "bad nonce"));
        var (state, _) = await StartAsync();

        var result = await CompleteAsync(state);

        result.ErrorCode.Should().Be(OidcErrorCodes.IdTokenInvalid);
        await _signIn.DidNotReceive().SignInAsync(Arg.Any<Guid>(), Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task User_info_is_asked_for_only_when_the_token_lacks_an_email()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
        var (state, _) = await StartAsync();
        await CompleteAsync(state);
        await _client.DidNotReceive().GetUserInfoAsync(Arg.Any<OidcDiscovery>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _validator.ValidateAsync(_provider, _discovery, "id-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, object> { ["sub"] = "subject-1" });
        var (second, _) = await StartAsync();
        await CompleteAsync(second);

        await _client.Received(1).GetUserInfoAsync(_discovery, "access-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_person_who_still_has_a_step_to_take_is_given_a_handle_and_nothing_is_signed_in()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.NeedsLinkConfirmation(passwordRejected: false));
        var (state, _) = await StartAsync();

        var result = await CompleteAsync(state);

        result.Outcome.Should().Be(OidcCallbackOutcome.Pending);
        OidcRedirects.Destination(result).Should().Be($"https://vora.example.com/login/external?pending={Uri.EscapeDataString(result.PendingHandle!)}");
        var described = _manager.DescribePending(result.PendingHandle!)!;
        described.Outcome.Should().Be(ExternalSignInOutcome.NeedsLinkConfirmation);
        described.ProviderName.Should().Be("Authentik");
        described.Email.Should().Be("sam@example.com");
    }

    [Fact]
    public async Task A_refusal_is_kept_so_the_page_can_say_why()
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.Refused(ExternalSignInRefusal.GroupNotAllowed, "Not in the group."));
        var (state, _) = await StartAsync();

        var result = await CompleteAsync(state);

        var described = _manager.DescribePending(result.PendingHandle!)!;
        described.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        described.Refusal.Should().Be(ExternalSignInRefusal.GroupNotAllowed);
        described.Message.Should().Be("Not in the group.");
    }

    [Fact]
    public void An_unknown_handle_describes_nothing()
    {
        _manager.DescribePending("guess").Should().BeNull();
    }

    private async Task<string> PendingHandleAsync(ExternalSignInResult first)
    {
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Is<ExternalSignInOptions>(o => !o.CreateAccount && o.LinkTo == null && o.InvitePin == null), Arg.Any<CancellationToken>()).Returns(first);
        var (state, _) = await StartAsync(new OidcStartRequest("/", "invite", null));
        return (await CompleteAsync(state)).PendingHandle!;
    }

    [Fact]
    public async Task Choosing_to_create_an_account_finishes_the_sign_in_with_a_code()
    {
        var handle = await PendingHandleAsync(ExternalSignInResult.NeedsCreateOrLink(true, ExternalSignInRefusal.None, null, false));
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Is<ExternalSignInOptions>(o => o.CreateAccount && o.InviteToken == "invite"), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.SignedIn(_userId, true));

        var result = await _manager.ResumeAsync(new ExternalCompleteRequest { Handle = handle, CreateAccount = true }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        result.IsNewAccount.Should().BeTrue();
        result.ReturnTo.Should().Be("/");
        _codes.Redeem(result.Code!).Should().Be(new ExternalSignInGrant(_userId, _provider.Id));
        _manager.DescribePending(handle).Should().BeNull();
    }

    [Fact]
    public async Task The_typed_password_and_pin_are_passed_on_and_the_handle_is_kept_until_it_works()
    {
        var handle = await PendingHandleAsync(ExternalSignInResult.NeedsLinkConfirmation(false));
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Is<ExternalSignInOptions>(o => o.LinkTo != null), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.NeedsLinkConfirmation(passwordRejected: true));

        var wrong = await _manager.ResumeAsync(new ExternalCompleteRequest { Handle = handle, LinkEmail = " sam@example.com ", LinkPassword = "nope" }, TestContext.Current.CancellationToken);

        wrong.Outcome.Should().Be(ExternalSignInOutcome.NeedsLinkConfirmation);
        wrong.PasswordRejected.Should().BeTrue();
        wrong.Code.Should().BeNull();
        _manager.DescribePending(handle).Should().NotBeNull();
        await _signIn.Received(1).SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Is<ExternalSignInOptions>(o => o.LinkTo!.Email == "sam@example.com" && o.LinkTo.Password == "nope"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Too_many_wrong_attempts_end_the_pending_sign_in()
    {
        var handle = await PendingHandleAsync(ExternalSignInResult.NeedsInvitePin(false));
        _signIn.SignInAsync(_provider.Id, Arg.Any<ExternalClaims>(), Arg.Is<ExternalSignInOptions>(o => o.InvitePin == "0000"), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.NeedsInvitePin(pinRejected: true));
        ExternalCompleteVM? last = null;

        for (var attempt = 0; attempt < ExternalPendingStore.MaxFailures; attempt++)
        {
            last = await _manager.ResumeAsync(new ExternalCompleteRequest { Handle = handle, CreateAccount = true, InvitePin = "0000" }, TestContext.Current.CancellationToken);
        }

        last!.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        last.Message.Should().Contain("Too many");
        _manager.DescribePending(handle).Should().BeNull();
        (await _manager.ResumeAsync(new ExternalCompleteRequest { Handle = handle, CreateAccount = true, InvitePin = "1234" }, TestContext.Current.CancellationToken)).Outcome.Should().Be(ExternalSignInOutcome.Refused);
    }

    [Fact]
    public async Task An_unknown_or_expired_handle_cannot_be_resumed()
    {
        var result = await _manager.ResumeAsync(new ExternalCompleteRequest { Handle = "guess", CreateAccount = true }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        result.Code.Should().BeNull();
        await _signIn.DidNotReceive().SignInAsync(Arg.Any<Guid>(), Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Linking_from_Account_settings_adds_the_provider_to_the_signed_in_account_and_signs_nobody_in()
    {
        _signIn.LinkAccountAsync(_provider.Id, _userId, Arg.Any<ExternalClaims>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(_userId, false));
        var (state, _) = await StartAsync(new OidcStartRequest("/account", null, _userId));

        var result = await CompleteAsync(state);

        result.Outcome.Should().Be(OidcCallbackOutcome.Linked);
        OidcRedirects.Destination(result).Should().Be("https://vora.example.com/account?linked=authentik");
        await _signIn.DidNotReceive().SignInAsync(Arg.Any<Guid>(), Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ExternalSignInRefusal.AlreadyLinked, OidcErrorCodes.LinkConflict)]
    [InlineData(ExternalSignInRefusal.GroupNotAllowed, OidcErrorCodes.LinkRefused)]
    public async Task A_link_that_cannot_be_made_goes_back_to_Account_settings_with_the_reason(ExternalSignInRefusal refusal, string expected)
    {
        _signIn.LinkAccountAsync(_provider.Id, _userId, Arg.Any<ExternalClaims>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.Refused(refusal, "no"));
        var (state, _) = await StartAsync(new OidcStartRequest(null, null, _userId));

        var result = await CompleteAsync(state);

        result.Outcome.Should().Be(OidcCallbackOutcome.Failed);
        OidcRedirects.Destination(result).Should().Be($"https://vora.example.com/account?linkError={expected}");
    }

    [Fact]
    public async Task A_provider_error_during_a_link_goes_back_to_Account_settings_not_the_login_page()
    {
        var (state, _) = await StartAsync(new OidcStartRequest(null, null, _userId));

        var result = await CompleteAsync(state, code: null, error: "access_denied");

        OidcRedirects.Destination(result).Should().Be("https://vora.example.com/account?linkError=access_denied");
    }

    [Fact]
    public void A_start_that_fails_goes_to_the_login_page_with_the_reason()
    {
        OidcRedirects.StartFailure(new OidcStartResult(OidcStartOutcome.Failed, null, OidcErrorCodes.DiscoveryFailed, "https://vora.example.com/"))
            .Should().Be("https://vora.example.com/login/external?error=discovery_failed");
    }

    [Fact]
    public async Task The_connection_test_reports_the_endpoints_the_keys_and_what_to_fix()
    {
        _provider.Issuer = "http://192.168.0.20:9000/application/o/vora/";
        _provider.ClientSecretCiphertext = null;
        _client.GetDiscoveryAsync(_provider, true, Arg.Any<CancellationToken>()).Returns(_discovery with { SupportsS256 = true, AdvertisesS256 = false });
        _client.GetSigningKeysAsync(Arg.Any<OidcDiscovery>(), true, Arg.Any<CancellationToken>()).Returns(new List<SecurityKey> { MockOidcProvider.AnotherKey(), MockOidcProvider.AnotherKey("key-2") });
        _providers.GetByIdAsync(_provider.Id).Returns(_provider);

        var result = (await _manager.TestAsync(_provider.Id, TestContext.Current.CancellationToken))!;

        result.Ok.Should().BeTrue();
        result.TokenEndpoint.Should().EndWith("/token");
        result.SigningKeyCount.Should().Be(2);
        result.Warnings.Should().Contain(w => w.Contains("plain http"))
            .And.Contain(w => w.Contains("PKCE methods"))
            .And.Contain(w => w.Contains("No client secret"));
    }

    [Fact]
    public async Task The_connection_test_reports_a_failure_instead_of_throwing()
    {
        _providers.GetByIdAsync(_provider.Id).Returns(_provider);
        _client.GetDiscoveryAsync(_provider, true, Arg.Any<CancellationToken>()).Returns<OidcDiscovery>(_ => throw new OidcProviderException(OidcErrorCodes.DiscoveryFailed, "The discovery document names the issuer \"x\", not the one entered."));

        var result = (await _manager.TestAsync(_provider.Id, TestContext.Current.CancellationToken))!;

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("not the one entered");
    }

    [Fact]
    public async Task The_connection_test_of_an_unknown_provider_is_nothing_and_of_an_empty_one_asks_for_the_basics()
    {
        (await _manager.TestAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();

        _provider.Issuer = null;
        _providers.GetByIdAsync(_provider.Id).Returns(_provider);
        (await _manager.TestAsync(_provider.Id, TestContext.Current.CancellationToken))!.Message.Should().Contain("issuer");
    }
}
