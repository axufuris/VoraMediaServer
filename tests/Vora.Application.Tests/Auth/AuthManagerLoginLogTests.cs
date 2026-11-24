using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth;

// A failed sign-in is logged with its reason, so an admin can tell a mistyped
// email from a wrong password; the client only ever gets a bare 401. No entry
// holds the email that was typed or the password: a known account is named by
// its id.
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

    private static readonly Guid AccountId = Guid.NewGuid();

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
            Substitute.For<IClientDefaultsManager>(),
            new MemoryCache(new MemoryCacheOptions()),
            _log);

        _users.GetUserWithProfilesByEmailAsync(Email).Returns(new User
        {
            Id = AccountId,
            Email = Email,
            DisplayName = "Reviewer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password)
        });
    }

    [Fact]
    public async Task An_unknown_email_is_logged_as_no_account_without_the_address()
    {
        var result = await _manager.LoginAsync("someone@example.com", Password);

        result.Should().BeNull();
        var entry = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        entry.Message.Should().Contain("no account");
        entry.Message.Should().NotContain("someone").And.NotContain("@");
    }

    [Fact]
    public async Task A_wrong_password_names_the_account_by_id_with_the_length_but_never_the_text()
    {
        var result = await _manager.LoginAsync(Email, "wrongpass");

        result.Should().BeNull();
        var entry = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        entry.Message.Should().Contain(AccountId.ToString()).And.Contain("wrong password").And.Contain("9 characters");
        entry.Message.Should().NotContain("wrongpass").And.NotContain(Email);
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
        var entry = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("locked out")).Subject;
        entry.Message.Should().NotContain(Email);
    }

    [Fact]
    public async Task A_successful_sign_in_logs_no_warning()
    {
        var result = await _manager.LoginAsync(Email, Password);

        result.Should().NotBeNull();
        _log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }
}
