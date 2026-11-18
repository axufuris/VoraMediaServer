using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth;
using Vora.Application.Auth.External;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth;

public class ExternalSignInServiceTests
{
    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IAuthManager _authManager = Substitute.For<IAuthManager>();
    private readonly IInvitationManager _invitations = Substitute.For<IInvitationManager>();
    private readonly ExternalSignInService _service;
    private readonly AuthProvider _provider = new() { Slug = "authentik", DisplayName = "Authentik", Enabled = true };
    private readonly ServerSetting _serverSettings = new() { RegistrationMode = RegistrationMode.Simple };

    public ExternalSignInServiceTests()
    {
        _service = new ExternalSignInService(_providers, _users, _settings, _authManager, _invitations, NullLogger<ExternalSignInService>.Instance);
        _providers.GetByIdAsync(_provider.Id).Returns(_provider);
        _settings.GetSettingsAsync().Returns(_serverSettings);
        _authManager.CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns(call => new User { Email = call.ArgAt<string>(0), DisplayName = call.ArgAt<string>(1), IsAdmin = call.ArgAt<bool>(2) });
    }

    private static ExternalClaims Claims(string? email = "sam@example.com", bool verified = true, string[]? groups = null) =>
        new("subject-1", email, verified, "Sam", groups ?? []);

    private static ExternalSignInOptions Options(bool create = false, string? pin = null, string? token = null, LocalCredentials? link = null) =>
        new() { CreateAccount = create, InvitePin = pin, InviteToken = token, LinkTo = link };

    private User ExistingUser(bool hasPassword = true, bool isAdmin = false, string email = "sam@example.com")
    {
        var user = new User { Email = email, DisplayName = "Sam", HasPassword = hasPassword, IsAdmin = isAdmin };
        _users.GetUserWithProfilesByEmailAsync(email).Returns(user);
        _users.GetUserByIdAsync(user.Id).Returns(user);
        return user;
    }

    private UserExternalIdentity LinkedIdentity(User user)
    {
        var identity = new UserExternalIdentity { UserId = user.Id, ProviderId = _provider.Id, Subject = "subject-1", Email = "old@example.com" };
        _providers.GetIdentityAsync(_provider.Id, "subject-1").Returns(identity);
        _users.GetUserByIdAsync(user.Id).Returns(user);
        return identity;
    }

    [Fact]
    public async Task An_unknown_provider_is_refused()
    {
        var result = await _service.SignInAsync(Guid.NewGuid(), Claims(), Options(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        result.Refusal.Should().Be(ExternalSignInRefusal.ProviderUnavailable);
    }

    [Fact]
    public async Task A_linked_account_signs_in_and_its_use_is_recorded()
    {
        var user = ExistingUser();
        var identity = LinkedIdentity(user);

        var result = await _service.SignInAsync(_provider.Id, Claims(email: "New@Example.com"), Options(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        result.UserId.Should().Be(user.Id);
        result.IsNewAccount.Should().BeFalse();
        identity.LastSignInAt.Should().NotBeNull();
        identity.Email.Should().Be("new@example.com");
        await _providers.Received(1).UpdateIdentityAsync(identity);
        await _authManager.DidNotReceive().CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task The_admin_group_makes_a_linked_account_an_administrator_and_ends_its_old_sessions()
    {
        _provider.AdminGroup = "vora-admins";
        var user = ExistingUser();
        var oldStamp = user.SecurityStamp;
        LinkedIdentity(user);

        await _service.SignInAsync(_provider.Id, Claims(groups: ["vora-admins"]), Options(), TestContext.Current.CancellationToken);

        user.IsAdmin.Should().BeTrue();
        user.SecurityStamp.Should().NotBe(oldStamp);
        await _users.Received(1).UpdateUserAsync(user);
    }

    [Fact]
    public async Task Without_the_admin_group_nobody_becomes_an_administrator_and_nobody_loses_it()
    {
        _provider.AdminGroup = "vora-admins";
        var plain = ExistingUser();
        LinkedIdentity(plain);
        var admin = new User { Email = "boss@example.com", DisplayName = "Boss", IsAdmin = true };
        var adminIdentity = new UserExternalIdentity { UserId = admin.Id, ProviderId = _provider.Id, Subject = "subject-2" };
        _providers.GetIdentityAsync(_provider.Id, "subject-2").Returns(adminIdentity);
        _users.GetUserByIdAsync(admin.Id).Returns(admin);

        await _service.SignInAsync(_provider.Id, Claims(groups: ["vora-users"]), Options(), TestContext.Current.CancellationToken);
        await _service.SignInAsync(_provider.Id, new ExternalClaims("subject-2", "boss@example.com", true, "Boss", []), Options(), TestContext.Current.CancellationToken);

        plain.IsAdmin.Should().BeFalse();
        admin.IsAdmin.Should().BeTrue();
        await _users.DidNotReceive().UpdateUserAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task An_account_an_admin_added_without_a_password_links_on_the_first_verified_sign_in()
    {
        var user = ExistingUser(hasPassword: false);

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        result.UserId.Should().Be(user.Id);
        await _providers.Received(1).AddIdentityAsync(Arg.Is<UserExternalIdentity>(i => i.UserId == user.Id && i.ProviderId == _provider.Id && i.Subject == "subject-1" && i.Email == "sam@example.com"));
        await _authManager.DidNotReceive().CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task An_unverified_email_never_links_to_the_account_that_uses_it()
    {
        ExistingUser();

        var result = await _service.SignInAsync(_provider.Id, Claims(verified: false), Options(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        result.Refusal.Should().Be(ExternalSignInRefusal.EmailNotVerified);
        await _providers.DidNotReceive().AddIdentityAsync(Arg.Any<UserExternalIdentity>());
    }

    [Fact]
    public async Task A_local_account_with_a_password_asks_for_it_once_and_links_on_the_right_one()
    {
        var user = ExistingUser();
        var credentials = new LocalCredentials("sam@example.com", "right-password");
        _authManager.VerifyCredentialsAsync("sam@example.com", "right-password").Returns(user.Id);

        var asked = await _service.SignInAsync(_provider.Id, Claims(), Options(), TestContext.Current.CancellationToken);
        var linked = await _service.SignInAsync(_provider.Id, Claims(), Options(link: credentials), TestContext.Current.CancellationToken);

        asked.Outcome.Should().Be(ExternalSignInOutcome.NeedsLinkConfirmation);
        linked.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        linked.UserId.Should().Be(user.Id);
        await _providers.Received(1).AddIdentityAsync(Arg.Is<UserExternalIdentity>(i => i.UserId == user.Id));
    }

    [Fact]
    public async Task A_wrong_local_password_links_nothing_and_asks_again()
    {
        ExistingUser();
        _authManager.VerifyCredentialsAsync("sam@example.com", "wrong").Returns((Guid?)null);

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(link: new LocalCredentials("sam@example.com", "wrong")), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.NeedsLinkConfirmation);
        result.PasswordRejected.Should().BeTrue();
        await _providers.DidNotReceive().AddIdentityAsync(Arg.Any<UserExternalIdentity>());
    }

    [Fact]
    public async Task Automatic_linking_skips_the_password_for_a_verified_email()
    {
        _provider.LinkByEmail = AuthLinkByEmailPolicy.Automatic;
        var user = ExistingUser();

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        result.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task A_new_person_is_asked_to_create_or_link_and_then_gets_an_account_with_a_first_profile()
    {
        var asked = await _service.SignInAsync(_provider.Id, Claims(), Options(), TestContext.Current.CancellationToken);
        var created = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true), TestContext.Current.CancellationToken);

        asked.Outcome.Should().Be(ExternalSignInOutcome.NeedsCreateOrLink);
        asked.CanCreateAccount.Should().BeTrue();
        created.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        created.IsNewAccount.Should().BeTrue();
        await _authManager.Received(1).CreateExternalUserAsync("sam@example.com", "Sam", false);
        await _providers.Received(1).AddIdentityAsync(Arg.Is<UserExternalIdentity>(i => i.ProviderId == _provider.Id && i.Subject == "subject-1"));
    }

    [Fact]
    public async Task A_new_account_in_the_admin_group_is_an_administrator()
    {
        _provider.AdminGroup = "vora-admins";

        await _service.SignInAsync(_provider.Id, Claims(groups: ["vora-admins"]), Options(create: true), TestContext.Current.CancellationToken);

        await _authManager.Received(1).CreateExternalUserAsync("sam@example.com", "Sam", true);
    }

    [Fact]
    public async Task With_registration_disabled_no_account_is_created()
    {
        _serverSettings.RegistrationMode = RegistrationMode.Disabled;

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        result.Refusal.Should().Be(ExternalSignInRefusal.RegistrationClosed);
        await _authManager.DidNotReceive().CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task In_invite_PIN_mode_the_PIN_is_used_up_only_when_the_account_is_created()
    {
        _serverSettings.RegistrationMode = RegistrationMode.SecretWord;
        var ticket = new RegistrationTicket { SecretCode = "1234", ExpiresAt = DateTime.UtcNow.AddMinutes(5) };
        _users.GetRegistrationTicketAsync("1234").Returns(ticket);

        var noPin = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true), TestContext.Current.CancellationToken);
        var wrong = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true, pin: "9999"), TestContext.Current.CancellationToken);
        await _users.DidNotReceive().DeleteRegistrationTicketAsync(Arg.Any<RegistrationTicket>());
        var created = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true, pin: "1234"), TestContext.Current.CancellationToken);

        noPin.Outcome.Should().Be(ExternalSignInOutcome.NeedsInvitePin);
        noPin.InvitePinRejected.Should().BeFalse();
        wrong.Outcome.Should().Be(ExternalSignInOutcome.NeedsInvitePin);
        wrong.InvitePinRejected.Should().BeTrue();
        created.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        await _users.Received(1).DeleteRegistrationTicketAsync(ticket);
    }

    [Fact]
    public async Task An_expired_invite_PIN_is_not_accepted()
    {
        _serverSettings.RegistrationMode = RegistrationMode.SecretWord;
        _users.GetRegistrationTicketAsync("1234").Returns(new RegistrationTicket { SecretCode = "1234", ExpiresAt = DateTime.UtcNow.AddMinutes(-1) });

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true, pin: "1234"), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.NeedsInvitePin);
        result.InvitePinRejected.Should().BeTrue();
        await _authManager.DidNotReceive().CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task In_invitation_mode_the_invitation_for_the_verified_email_is_used_up_and_makes_the_account()
    {
        _serverSettings.RegistrationMode = RegistrationMode.Invitation;
        var invitation = new InvitationTicket { Email = "sam@example.com", TokenHash = "hash-1", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _invitations.ValidateTokenAsync("invite-token").Returns(invitation);

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(token: "invite-token"), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.SignedIn);
        result.IsNewAccount.Should().BeTrue();
        await _invitations.Received(1).ConsumeAsync("hash-1");
    }

    [Fact]
    public async Task An_invitation_for_another_address_creates_nothing_and_is_not_used_up()
    {
        _serverSettings.RegistrationMode = RegistrationMode.Invitation;
        _invitations.ValidateTokenAsync("invite-token").Returns(new InvitationTicket { Email = "someone-else@example.com", TokenHash = "hash-1", ExpiresAt = DateTime.UtcNow.AddDays(1) });

        var result = await _service.SignInAsync(_provider.Id, Claims(), Options(create: true, token: "invite-token"), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ExternalSignInOutcome.Refused);
        result.Refusal.Should().Be(ExternalSignInRefusal.InvitationMismatch);
        await _invitations.DidNotReceive().ConsumeAsync(Arg.Any<string>());
        await _authManager.DidNotReceive().CreateExternalUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_name_the_provider_did_not_send_falls_back_to_the_start_of_the_email()
    {
        await _service.SignInAsync(_provider.Id, new ExternalClaims("subject-1", "sam@example.com", true, null, []), Options(create: true), TestContext.Current.CancellationToken);

        await _authManager.Received(1).CreateExternalUserAsync("sam@example.com", string.Empty, false);
    }
}
