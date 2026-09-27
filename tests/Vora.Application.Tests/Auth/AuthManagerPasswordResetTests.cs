using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth;

public class AuthManagerPasswordResetTests
{
    private readonly IUserRepository _users;
    private readonly ISystemSettingsRepository _settings;
    private readonly IEmailService _email;
    private readonly IInvitationManager _invitations;
    private readonly IMemoryCache _cache;
    private readonly AuthManager _manager;

    public AuthManagerPasswordResetTests()
    {
        _users = Substitute.For<IUserRepository>();
        _settings = Substitute.For<ISystemSettingsRepository>();
        _email = Substitute.For<IEmailService>();
        _invitations = Substitute.For<IInvitationManager>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SecretKey = new string('x', 64)
        });

        _manager = new AuthManager(
            _users,
            _settings,
            jwtOptions,
            _email,
            _invitations,
            _cache,
            NullLogger<AuthManager>.Instance);
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task ConfirmPasswordResetAsync_returns_InvalidToken_when_token_empty(string token)
    {
        var result = await _manager.ConfirmPasswordResetAsync(token, "ValidPassword123", TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.InvalidToken);
        await _users.DidNotReceive().UpdateUserAsync(Arg.Any<User>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("       ")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task ConfirmPasswordResetAsync_returns_PasswordRejected_for_invalid_password(string password)
    {
        var result = await _manager.ConfirmPasswordResetAsync("valid-token", password, TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.PasswordRejected);
        await _users.DidNotReceive().UpdateUserAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_accepts_exactly_eight_chars()
    {
        var token = "good-token";
        var hash = HashToken(token);
        var user = NewUser();
        _users.GetActivePasswordResetTicketByHashAsync(hash)
            .Returns(new PasswordResetTicket { UserId = user.Id, TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _users.GetUserByIdAsync(user.Id).Returns(user);

        var result = await _manager.ConfirmPasswordResetAsync(token, "12345678", TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.Success);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_returns_InvalidToken_when_ticket_missing()
    {
        _users.GetActivePasswordResetTicketByHashAsync(Arg.Any<string>())
            .Returns((PasswordResetTicket?)null);

        var result = await _manager.ConfirmPasswordResetAsync("missing-token", "ValidPassword123", TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.InvalidToken);
        await _users.DidNotReceive().UpdateUserAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_returns_InvalidToken_and_deletes_ticket_when_user_missing()
    {
        var token = "valid-token";
        var hash = HashToken(token);
        var ticket = new PasswordResetTicket
        {
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        _users.GetActivePasswordResetTicketByHashAsync(hash).Returns(ticket);
        _users.GetUserByIdAsync(ticket.UserId).Returns((User?)null);

        var result = await _manager.ConfirmPasswordResetAsync(token, "ValidPassword123", TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.InvalidToken);
        await _users.Received(1).DeletePasswordResetTicketAsync(ticket);
        await _users.DidNotReceive().UpdateUserAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_rotates_password_and_security_stamp_on_success()
    {
        var token = "good-token";
        var hash = HashToken(token);
        var user = NewUser();
        var originalStamp = user.SecurityStamp;
        var originalHash = user.PasswordHash;

        _users.GetActivePasswordResetTicketByHashAsync(hash)
            .Returns(new PasswordResetTicket { UserId = user.Id, TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddMinutes(30) });
        _users.GetUserByIdAsync(user.Id).Returns(user);

        var result = await _manager.ConfirmPasswordResetAsync(token, "BrandNewPassword99", TestContext.Current.CancellationToken);

        result.Should().Be(PasswordResetResult.Success);
        user.PasswordHash.Should().NotBe(originalHash);
        BCrypt.Net.BCrypt.Verify("BrandNewPassword99", user.PasswordHash).Should().BeTrue();
        user.SecurityStamp.Should().NotBe(originalStamp);
        await _users.Received(1).UpdateUserAsync(user);
        await _users.Received(1).InvalidateOutstandingPasswordResetTicketsForUserAsync(user.Id);
    }

    private static User NewUser() => new()
    {
        Email = "test@example.com",
        DisplayName = "Tester",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("OldPassword123")
    };
}
