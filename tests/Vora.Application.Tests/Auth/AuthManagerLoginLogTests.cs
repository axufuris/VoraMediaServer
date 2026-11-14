using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth;

// A failed sign-in is logged with its reason (and never the password), so an
// admin can tell a mistyped email from a wrong password; the client only ever
// gets a bare 401.
public class AuthManagerLoginLogTests
{
    private sealed class ListLogger : ILogger<AuthManager>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private const string Email = "reviewer@example.com";
    private const string Password = "CorrectHorse12";

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ListLogger _log = new();
    private readonly AuthManager _manager;

    public AuthManagerLoginLogTests()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SecretKey = new string('x', 64)
        });

        _manager = new AuthManager(
            _users,
            Substitute.For<ISystemSettingsRepository>(),
            jwtOptions,
            Substitute.For<IEmailService>(),
            Substitute.For<IInvitationManager>(),
            new MemoryCache(new MemoryCacheOptions()),
            _log);

        _users.GetUserWithProfilesByEmailAsync(Email).Returns(new User
        {
            Id = Guid.NewGuid(),
            Email = Email,
            DisplayName = "Reviewer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password)
        });
    }

    [Fact]
    public async Task An_unknown_email_is_logged_as_no_account()
    {
        var result = await _manager.LoginAsync("someone@example.com", Password);

        result.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("'someone@example.com'")
            && e.Message.Contains("no account"));
    }

    [Fact]
    public async Task A_wrong_password_is_logged_with_its_length_but_never_its_text()
    {
        var result = await _manager.LoginAsync(Email, "wrongpass");

        result.Should().BeNull();
        var entry = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        entry.Message.Should().Contain($"'{Email}'").And.Contain("wrong password").And.Contain("9 characters");
        entry.Message.Should().NotContain("wrongpass");
    }

    [Fact]
    public async Task A_locked_out_email_is_logged_as_locked_out()
    {
        for (var i = 0; i < 5; i++)
        {
            await _manager.LoginAsync(Email, "wrongpass");
        }
        _log.Entries.Clear();

        var result = await _manager.LoginAsync(Email, Password);

        result.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("locked out"));
    }

    [Fact]
    public async Task A_successful_sign_in_logs_no_warning()
    {
        var result = await _manager.LoginAsync(Email, Password);

        result.Should().NotBeNull();
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }
}
