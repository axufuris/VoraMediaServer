using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External;
using Vora.Application.Auth.External.Ldap;
using Vora.Application.Auth.External.Oidc;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth.Ldap;

public class LdapSignInManagerTests
{
    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly IAuthSecretProtector _secrets = Substitute.For<IAuthSecretProtector>();
    private readonly ILdapDirectory _directory = Substitute.For<ILdapDirectory>();
    private readonly IExternalSignInService _signIn = Substitute.For<IExternalSignInService>();
    private readonly ExternalPendingStore _pendings = new(TimeProvider.System);
    private readonly LdapSignInManager _manager;

    public LdapSignInManagerTests()
    {
        _manager = new LdapSignInManager(_providers, _secrets, _directory, _signIn, _pendings, NullLogger<LdapSignInManager>.Instance);
        _secrets.Unprotect(Arg.Any<string>()).Returns(call => call.ArgAt<string>(0).Replace("sealed:", string.Empty, StringComparison.Ordinal));
    }

    private static AuthProvider Directory(string slug = "lldap", Action<AuthProvider>? change = null)
    {
        var provider = new AuthProvider
        {
            Type = AuthProviderType.Ldap,
            Slug = slug,
            DisplayName = "Company directory",
            Enabled = true,
            LdapHost = "ldap.example.org",
            LdapSecurity = LdapSecurityMode.Ldaps,
            LdapBindDn = "cn=svc,dc=example,dc=org",
            LdapBindPasswordCiphertext = "sealed:bind-secret",
            LdapUserBaseDn = "ou=people,dc=example,dc=org",
            LdapUserFilter = "(&(objectClass=person)(uid={username}))"
        };
        change?.Invoke(provider);
        return provider;
    }

    private static LdapUserEntry Alice(string? mail = "alice@example.org") =>
        new("uid=alice,ou=people,dc=example,dc=org", "uuid-alice", mail, "Alice Example", ["cn=staff,ou=groups,dc=example,dc=org"]);

    private void Enabled(params AuthProvider[] providers) => _providers.GetEnabledAsync().Returns(providers.ToList());

    [Fact]
    public async Task Only_enabled_directories_count()
    {
        Enabled(new AuthProvider { Slug = "authentik", DisplayName = "A", Type = AuthProviderType.Oidc, Enabled = true });
        (await _manager.AnyEnabledAsync()).Should().BeFalse();

        Enabled(Directory());
        (await _manager.AnyEnabledAsync()).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "pw")]
    [InlineData("   ", "pw")]
    [InlineData("alice", "")]
    [InlineData("al\nice", "pw")]
    public async Task A_blank_or_odd_name_or_an_empty_password_never_reaches_the_directory(string name, string password)
    {
        Enabled(Directory());

        var result = await _manager.SignInAsync(name, password, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LdapSignInOutcome.InvalidCredentials);
        await _directory.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task The_directory_is_asked_with_the_saved_settings_and_the_unsealed_bind_password()
    {
        Enabled(Directory(change: p => { p.LdapCaCertificatePem = "pem"; p.LdapPort = 6360; p.LdapMailAttribute = "email"; p.LdapGroupBaseDn = "ou=groups,dc=x"; p.LdapGroupFilter = "(member={dn})"; }));
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LdapAuthResult.InvalidCredentials("no"));

        await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        await _directory.Received(1).AuthenticateAsync(
            Arg.Is<LdapConnectionSettings>(s =>
                s.Host == "ldap.example.org" && s.Port == 6360 && s.Security == LdapSecurityMode.Ldaps && s.CaCertificatePem == "pem"
                && s.BindDn == "cn=svc,dc=example,dc=org" && s.BindPassword == "bind-secret" && s.MailAttribute == "email"
                && s.UserBaseDn == "ou=people,dc=example,dc=org" && s.GroupBaseDn == "ou=groups,dc=x" && s.GroupFilter == "(member={dn})"),
            "alice", "pw", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_default_port_follows_the_way_the_connection_is_secured()
    {
        Enabled(Directory(change: p => p.LdapSecurity = LdapSecurityMode.StartTls));
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LdapAuthResult.InvalidCredentials("no"));

        await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        await _directory.Received(1).AuthenticateAsync(Arg.Is<LdapConnectionSettings>(s => s.Port == 389), "alice", "pw", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_or_unknown_name_is_just_invalid_credentials()
    {
        Enabled(Directory());
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LdapAuthResult.InvalidCredentials("the directory refused the password"));

        var result = await _manager.SignInAsync("alice", "wrong", TestContext.Current.CancellationToken);

        result.Should().BeSameAs(LdapSignInResult.Invalid);
        await _signIn.DidNotReceiveWithAnyArgs().SignInAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task A_directory_that_cannot_be_reached_is_skipped_for_the_next_one()
    {
        var down = Directory("down", p => p.SortOrder = 0);
        var up = Directory("up", p => p.SortOrder = 1);
        Enabled(up, down);
        _directory.AuthenticateAsync(Arg.Is<LdapConnectionSettings>(s => s.UserBaseDn == "ou=people,dc=example,dc=org"), "alice", "pw", Arg.Any<CancellationToken>())
            .Returns(LdapAuthResult.Unreachable("Could not connect"), LdapAuthResult.Authenticated(Alice()));
        var userId = Guid.NewGuid();
        _signIn.SignInAsync(up.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(userId, isNewAccount: false));

        var result = await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LdapSignInOutcome.SignedIn);
        result.UserId.Should().Be(userId);
        await _directory.Received(2).AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), "alice", "pw", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_directory_that_is_down_is_invalid_credentials_for_the_person()
    {
        Enabled(Directory());
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LdapAuthResult.Unreachable("Could not connect to ldap.example.org:636."));

        (await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken)).Outcome.Should().Be(LdapSignInOutcome.InvalidCredentials);
    }

    [Fact]
    public async Task A_directory_entry_goes_through_the_sign_in_rules_with_verified_claims()
    {
        var provider = Directory();
        Enabled(provider);
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), "alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapAuthResult.Authenticated(Alice()));
        var userId = Guid.NewGuid();
        _signIn.SignInAsync(provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(ExternalSignInResult.SignedIn(userId, isNewAccount: true));

        var result = await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Should().BeEquivalentTo(LdapSignInResult.SignedIn(userId));
        await _signIn.Received(1).SignInAsync(
            provider.Id,
            Arg.Is<ExternalClaims>(c => c.Subject == "uuid-alice" && c.Email == "alice@example.org" && c.EmailVerified && c.DisplayName == "Alice Example" && c.Groups.Contains("staff")),
            Arg.Any<ExternalSignInOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nobody")]
    public async Task An_entry_with_no_mail_is_refused_with_a_clear_message_and_never_made_an_account(string? mail)
    {
        Enabled(Directory());
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), "carol", "pw", Arg.Any<CancellationToken>()).Returns(LdapAuthResult.Authenticated(Alice(mail)));

        var result = await _manager.SignInAsync("carol", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LdapSignInOutcome.Refused);
        result.Message.Should().Be(LdapSignInManager.MailMissingMessage);
        await _signIn.DidNotReceiveWithAnyArgs().SignInAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData(ExternalSignInOutcome.NeedsLinkConfirmation)]
    [InlineData(ExternalSignInOutcome.NeedsInvitePin)]
    [InlineData(ExternalSignInOutcome.NeedsCreateOrLink)]
    public async Task A_step_that_is_left_becomes_a_pending_sign_in_the_web_can_resume(ExternalSignInOutcome outcome)
    {
        var provider = Directory();
        Enabled(provider);
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), "alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapAuthResult.Authenticated(Alice()));
        var step = outcome switch
        {
            ExternalSignInOutcome.NeedsLinkConfirmation => ExternalSignInResult.NeedsLinkConfirmation(passwordRejected: false),
            ExternalSignInOutcome.NeedsInvitePin => ExternalSignInResult.NeedsInvitePin(pinRejected: false),
            _ => ExternalSignInResult.NeedsCreateOrLink(canCreate: true, ExternalSignInRefusal.None, null, passwordRejected: false)
        };
        _signIn.SignInAsync(provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>()).Returns(step);

        var result = await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LdapSignInOutcome.Pending);
        result.PendingKind.Should().Be(outcome);
        var pending = _pendings.Find(result.PendingHandle!);
        pending.Should().NotBeNull();
        pending!.ProviderId.Should().Be(provider.Id);
        pending.ProviderName.Should().Be("Company directory");
        pending.Claims.Email.Should().Be("alice@example.org");
        pending.Last.Outcome.Should().Be(outcome);
        pending.InviteToken.Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_by_the_rules_is_passed_on_with_its_reason()
    {
        var provider = Directory();
        Enabled(provider);
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), "alice", "pw", Arg.Any<CancellationToken>()).Returns(LdapAuthResult.Authenticated(Alice()));
        _signIn.SignInAsync(provider.Id, Arg.Any<ExternalClaims>(), Arg.Any<ExternalSignInOptions>(), Arg.Any<CancellationToken>())
            .Returns(ExternalSignInResult.Refused(ExternalSignInRefusal.GroupNotAllowed, "You are not in the group that may sign in here."));

        var result = await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LdapSignInOutcome.Refused);
        result.Message.Should().Be("You are not in the group that may sign in here.");
    }

    [Fact]
    public async Task A_directory_without_its_host_or_search_settings_is_skipped()
    {
        Enabled(Directory(change: p => p.LdapHost = null), Directory("two", p => p.LdapUserFilter = " "), Directory("three", p => p.LdapUserBaseDn = null));

        (await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken)).Outcome.Should().Be(LdapSignInOutcome.InvalidCredentials);
        await _directory.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task A_bind_password_the_server_keys_cannot_read_skips_that_directory()
    {
        Enabled(Directory());
        _secrets.Unprotect(Arg.Any<string>()).Returns(_ => throw new CryptographicException("the key was not found"));

        (await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken)).Outcome.Should().Be(LdapSignInOutcome.InvalidCredentials);
        await _directory.DidNotReceiveWithAnyArgs().AuthenticateAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task A_directory_with_no_bind_account_is_searched_without_one()
    {
        Enabled(Directory(change: p => { p.LdapBindDn = null; p.LdapBindPasswordCiphertext = null; }));
        _directory.AuthenticateAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LdapAuthResult.InvalidCredentials("no"));

        await _manager.SignInAsync("alice", "pw", TestContext.Current.CancellationToken);

        await _directory.Received(1).AuthenticateAsync(Arg.Is<LdapConnectionSettings>(s => s.BindDn == null && s.BindPassword == null), "alice", "pw", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Testing_an_unknown_or_oidc_provider_finds_nothing()
    {
        _providers.GetByIdAsync(Arg.Any<Guid>()).Returns((AuthProvider?)null);
        (await _manager.TestAsync(Guid.NewGuid(), TestContext.Current.CancellationToken)).Should().BeNull();

        var oidc = new AuthProvider { Slug = "x", DisplayName = "X", Type = AuthProviderType.Oidc };
        _providers.GetByIdAsync(oidc.Id).Returns(oidc);
        (await _manager.TestAsync(oidc.Id, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Testing_a_directory_with_missing_settings_says_what_to_enter()
    {
        var provider = Directory(change: p => p.LdapHost = null);
        _providers.GetByIdAsync(provider.Id).Returns(provider);

        var result = await _manager.TestAsync(provider.Id, TestContext.Current.CancellationToken);

        result!.Ok.Should().BeFalse();
        result.Message.Should().Contain("host").And.Contain("search base");
        await _directory.DidNotReceiveWithAnyArgs().SampleAsync(default!, default);
    }

    private async Task<Application.Auth.ViewModels.LdapTestResultVM> TestAsync(LdapSampleResult sample, Action<AuthProvider>? change = null)
    {
        var provider = Directory(change: change);
        _providers.GetByIdAsync(provider.Id).Returns(provider);
        _directory.SampleAsync(Arg.Any<LdapConnectionSettings>(), Arg.Any<CancellationToken>()).Returns(sample);
        return (await _manager.TestAsync(provider.Id, TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task A_directory_that_answers_and_has_people_with_mail_tests_ok()
    {
        var result = await TestAsync(new LdapSampleResult(true, true, 5, false, 5, 0, 5, ["uid=a,dc=x"], null));

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("found 5 entries");
        result.Warnings.Should().BeEmpty();
        result.SampleDns.Should().Equal("uid=a,dc=x");
    }

    [Fact]
    public async Task A_directory_that_cannot_be_reached_or_bound_reports_why()
    {
        var unreachable = await TestAsync(new LdapSampleResult(false, false, 0, false, 0, 0, 0, [], "Could not connect to ldap.example.org:636."));
        unreachable.Ok.Should().BeFalse();
        unreachable.Connected.Should().BeFalse();
        unreachable.Message.Should().Be("Could not connect to ldap.example.org:636.");

        var refused = await TestAsync(new LdapSampleResult(true, false, 0, false, 0, 0, 0, [], "The bind account or its password was refused by the directory."));
        refused.Connected.Should().BeTrue();
        refused.ServiceBound.Should().BeFalse();
        refused.Message.Should().Contain("bind account");
    }

    [Fact]
    public async Task A_search_that_finds_nobody_is_not_ok()
    {
        var result = await TestAsync(new LdapSampleResult(true, true, 0, false, 0, 0, 0, [], null));

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("search base").And.Contain("filter");
    }

    [Fact]
    public async Task A_search_whose_entries_have_no_mail_attribute_is_not_ok_and_names_the_attribute()
    {
        var result = await TestAsync(new LdapSampleResult(true, true, 3, false, 0, 3, 3, [], null), p => p.LdapMailAttribute = "email");

        result.Ok.Should().BeFalse();
        result.Message.Should().Contain("mail attribute (email)");
    }

    [Fact]
    public async Task Some_entries_without_mail_and_a_missing_unique_id_are_warnings()
    {
        var result = await TestAsync(new LdapSampleResult(true, true, 4, true, 3, 1, 0, [], null));

        result.Ok.Should().BeTrue();
        result.Message.Should().Contain("at least 4");
        result.Warnings.Should().Contain(w => w.Contains("1 of the 4 entries"))
            .And.Contain(w => w.Contains("unique id attribute (entryUUID)"));
    }

    [Fact]
    public async Task An_unencrypted_connection_and_a_missing_bind_account_are_warned_about()
    {
        var result = await TestAsync(
            new LdapSampleResult(true, true, 2, false, 2, 0, 2, [], null),
            p => { p.LdapSecurity = LdapSecurityMode.None; p.LdapBindDn = null; p.LdapBindPasswordCiphertext = null; });

        result.Warnings.Should().Contain(w => w.Contains("not encrypted")).And.Contain(w => w.Contains("anonymously"));
    }
}
