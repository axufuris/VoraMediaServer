using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth;

public class AuthManagerPasswordSignInTests
{
    private const string Password = "CorrectHorse12";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _serverSettings = new() { RegistrationMode = RegistrationMode.Simple };
    private readonly AuthManager _manager;

    public AuthManagerPasswordSignInTests()
    {
        _settings.GetSettingsAsync().Returns(_serverSettings);
        _manager = new AuthManager(
            _users,
            _settings,
            Options.Create(new JwtOptions { Issuer = "test", Audience = "test", SecretKey = new string('x', 64) }),
            Substitute.For<IEmailService>(),
            Substitute.For<IInvitationManager>(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthManager>.Instance);
    }

    private User Account(string email, bool isAdmin = false, bool hasPassword = true)
    {
        var user = new User
        {
            Email = email,
            DisplayName = "Sam",
            IsAdmin = isAdmin,
            HasPassword = hasPassword,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password)
        };
        _users.GetUserWithProfilesByEmailAsync(email).Returns(user);
        _users.GetUserByIdAsync(user.Id).Returns(user);
        return user;
    }

    [Fact]
    public async Task With_password_sign_in_on_everyone_signs_in()
    {
        Account("sam@example.com");
        Account("boss@example.com", isAdmin: true);

        (await _manager.LoginAsync("sam@example.com", Password)).Should().NotBeNull();
        (await _manager.LoginAsync("boss@example.com", Password)).Should().NotBeNull();
    }

    [Fact]
    public async Task With_password_sign_in_off_an_ordinary_account_is_refused_with_a_clear_message()
    {
        _serverSettings.PasswordSignInEnabled = false;
        Account("sam@example.com");

        var login = () => _manager.LoginAsync("sam@example.com", Password);

        (await login.Should().ThrowAsync<PasswordSignInDisabledException>()).Which.Message.Should().Contain("sign-in providers");
    }

    [Fact]
    public async Task With_password_sign_in_off_an_administrator_still_signs_in()
    {
        _serverSettings.PasswordSignInEnabled = false;
        Account("boss@example.com", isAdmin: true);

        (await _manager.LoginAsync("boss@example.com", Password)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_wrong_password_is_still_a_plain_failure_when_password_sign_in_is_off()
    {
        _serverSettings.PasswordSignInEnabled = false;
        Account("sam@example.com");

        (await _manager.LoginAsync("sam@example.com", "wrong-password")).Should().BeNull();
    }

    [Fact]
    public async Task An_account_without_a_password_cannot_sign_in_with_one()
    {
        Account("sam@example.com", hasPassword: false);

        (await _manager.LoginAsync("sam@example.com", Password)).Should().BeNull();
        (await _manager.VerifyCredentialsAsync("sam@example.com", Password)).Should().BeNull();
    }

    [Fact]
    public async Task Verifying_credentials_is_not_a_sign_in_so_it_ignores_the_password_switch()
    {
        _serverSettings.PasswordSignInEnabled = false;
        var user = Account("sam@example.com");

        (await _manager.VerifyCredentialsAsync("sam@example.com", Password)).Should().Be(user.Id);
    }

    [Fact]
    public async Task Failed_verifications_count_towards_the_same_lockout_as_a_sign_in()
    {
        Account("sam@example.com");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await _manager.VerifyCredentialsAsync("sam@example.com", "wrong-password");
        }

        (await _manager.VerifyCredentialsAsync("sam@example.com", Password)).Should().BeNull();
        (await _manager.LoginAsync("sam@example.com", Password)).Should().BeNull();
    }

    [Fact]
    public async Task Registering_with_a_password_is_refused_while_password_sign_in_is_off()
    {
        _serverSettings.PasswordSignInEnabled = false;

        var register = () => _manager.RegisterAsync("new@example.com", Password, "New", null);

        (await register.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("sign-in providers");
        await _users.DidNotReceive().AddUserAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task An_admin_cannot_add_an_account_with_a_password_while_password_sign_in_is_off_but_can_add_one_without()
    {
        _serverSettings.PasswordSignInEnabled = false;

        var withPassword = await _manager.CreateUserAsAdminAsync("a@example.com", Password, "A");
        var without = await _manager.CreatePasswordlessUserAsAdminAsync("b@example.com", "B");

        withPassword.Outcome.Should().Be(AdminCreateUserOutcome.Invalid);
        without.Outcome.Should().Be(AdminCreateUserOutcome.Created);
    }

    [Fact]
    public async Task An_account_added_without_a_password_is_an_ordinary_account_with_a_first_profile_and_no_usable_password()
    {
        var result = await _manager.CreatePasswordlessUserAsAdminAsync(" Sam@Example.com ", " Sam ");

        result.Outcome.Should().Be(AdminCreateUserOutcome.Created);
        await _users.Received(1).AddUserAsync(Arg.Is<User>(u =>
            u.Email == "sam@example.com"
            && u.DisplayName == "Sam"
            && !u.HasPassword
            && !u.IsAdmin
            && u.PasswordHash.Length > 0
            && u.Profiles.Count == 1));
    }

    [Fact]
    public async Task Adding_a_passwordless_account_for_an_email_in_use_is_refused()
    {
        Account("sam@example.com");

        (await _manager.CreatePasswordlessUserAsAdminAsync("sam@example.com", "Sam")).Outcome.Should().Be(AdminCreateUserOutcome.EmailInUse);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_account_made_from_an_external_sign_in_has_the_defaults_of_a_registered_one(bool isAdmin)
    {
        var user = await _manager.CreateExternalUserAsync(" Sam@Example.com ", "  ", isAdmin);

        user.Email.Should().Be("sam@example.com");
        user.DisplayName.Should().Be("sam");
        user.HasPassword.Should().BeFalse();
        user.IsAdmin.Should().Be(isAdmin);
        user.Profiles.Should().ContainSingle().Which.Name.Should().Be("sam");
        user.CanRequestMedia.Should().Be(isAdmin);
        await _users.Received(1).AddUserAsync(user);
    }

    [Fact]
    public async Task Signing_in_an_external_account_gives_the_same_account_token_a_login_does()
    {
        var user = Account("sam@example.com");

        var external = await _manager.SignInExternalAsync(user.Id);
        var password = await _manager.LoginAsync("sam@example.com", Password);

        external.Should().NotBeNull();
        external!.UserId.Should().Be(password!.UserId);
        external.DisplayName.Should().Be(password.DisplayName);
        external.IsAdmin.Should().Be(password.IsAdmin);
        external.AccessToken.Split('.').Should().HaveCount(3);
        (await _manager.SignInExternalAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task A_refresh_token_from_a_password_sign_in_stops_working_for_non_admins_once_password_sign_in_is_off()
    {
        var user = Account("sam@example.com");
        var stored = new AuthRefreshToken { FamilyId = Guid.NewGuid(), UserId = user.Id, TokenHash = "x", SecurityStamp = user.SecurityStamp, ExpiresAt = DateTime.UtcNow.AddDays(1), SignInMethod = AuthSignInMethod.Password };
        _users.GetAuthRefreshTokenByHashAsync(Arg.Any<string>()).Returns(stored);
        _serverSettings.PasswordSignInEnabled = false;

        var result = await _manager.RefreshAsync("token", null, "device");

        result.Should().BeNull();
        await _users.Received(1).RevokeAuthRefreshTokenFamilyAsync(stored.FamilyId);
    }

    [Theory]
    [InlineData(AuthSignInMethod.External, false)]
    [InlineData(AuthSignInMethod.Password, true)]
    public async Task A_refresh_keeps_working_for_provider_sessions_and_for_admins_when_password_sign_in_is_off(AuthSignInMethod method, bool isAdmin)
    {
        var user = Account("sam@example.com", isAdmin: isAdmin);
        var stored = new AuthRefreshToken { FamilyId = Guid.NewGuid(), UserId = user.Id, TokenHash = "x", SecurityStamp = user.SecurityStamp, ExpiresAt = DateTime.UtcNow.AddDays(1), SignInMethod = method };
        _users.GetAuthRefreshTokenByHashAsync(Arg.Any<string>()).Returns(stored);
        _serverSettings.PasswordSignInEnabled = false;

        var result = await _manager.RefreshAsync("token", null, "device");

        result.Should().NotBeNull();
        await _users.Received(1).AddAuthRefreshTokenAsync(Arg.Is<AuthRefreshToken>(t => t.SignInMethod == method));
    }

    [Fact]
    public async Task A_refresh_token_records_how_the_session_began()
    {
        var user = Account("sam@example.com");

        await _manager.IssueRefreshTokenAsync(user.Id, "device", AuthSignInMethod.External);
        await _manager.IssueRefreshTokenAsync(user.Id, "device");

        await _users.Received(1).AddAuthRefreshTokenAsync(Arg.Is<AuthRefreshToken>(t => t.SignInMethod == AuthSignInMethod.External));
        await _users.Received(1).AddAuthRefreshTokenAsync(Arg.Is<AuthRefreshToken>(t => t.SignInMethod == AuthSignInMethod.Password));
    }
}
