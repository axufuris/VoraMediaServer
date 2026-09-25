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

// An admin can always add someone directly. The registration mode decides who
// may sign THEMSELVES up, so even with sign-up disabled this must work.
public class AdminCreateUserTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly AuthManager _manager;

    public AdminCreateUserTests()
    {
        _settings.GetSettingsAsync().Returns(new ServerSetting { RegistrationMode = RegistrationMode.Disabled });
        _manager = new AuthManager(
            _users,
            _settings,
            Options.Create(new JwtOptions { Issuer = "test", Audience = "test", SecretKey = new string('x', 64) }),
            Substitute.For<IEmailService>(),
            Substitute.For<IInvitationManager>(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthManager>.Instance);
    }

    [Fact]
    public async Task Creates_an_ordinary_account_with_a_first_profile_even_with_sign_up_disabled()
    {
        var result = await _manager.CreateUserAsAdminAsync(" Sam@Example.com ", "a-long-password", " Sam ");

        result.Outcome.Should().Be(AdminCreateUserOutcome.Created);
        await _users.Received(1).AddUserAsync(Arg.Is<User>(u =>
            u.Email == "sam@example.com"
            && u.DisplayName == "Sam"
            && !u.IsAdmin
            && u.Profiles.Count == 1
            && u.Profiles.First().Name == "Sam"));
    }

    [Fact]
    public async Task An_email_already_in_use_is_refused()
    {
        _users.GetUserWithProfilesByEmailAsync("sam@example.com").Returns(new User { Email = "sam@example.com", DisplayName = "Sam" });

        var result = await _manager.CreateUserAsAdminAsync("sam@example.com", "a-long-password", "Sam");

        result.Outcome.Should().Be(AdminCreateUserOutcome.EmailInUse);
        await _users.DidNotReceive().AddUserAsync(Arg.Any<User>());
    }

    [Theory]
    [InlineData("not-an-email", "a-long-password", "Sam")]
    [InlineData("sam@example.com", "short", "Sam")]
    [InlineData("sam@example.com", "a-long-password", "  ")]
    public async Task Bad_details_are_refused_with_a_reason(string email, string password, string name)
    {
        var result = await _manager.CreateUserAsAdminAsync(email, password, name);

        result.Outcome.Should().Be(AdminCreateUserOutcome.Invalid);
        result.Error.Should().NotBeNullOrWhiteSpace();
        await _users.DidNotReceive().AddUserAsync(Arg.Any<User>());
    }
}
