using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vora.Application.Auth;
using Vora.Application.Email;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;

namespace Vora.Application.Tests.Auth;

public class ClaimServerTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settingsRepo = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _settings = new() { ServerName = "Vora Server" };
    private readonly AuthManager _manager;

    public ClaimServerTests()
    {
        _users.HasAdminUserAsync().Returns(false);
        _settingsRepo.GetSettingsAsync().Returns(_settings);
        _settingsRepo.GetSettingsForUpdateAsync().Returns(_settings);
        _manager = new AuthManager(
            _users,
            _settingsRepo,
            Options.Create(new JwtOptions { Issuer = "test", Audience = "test", SecretKey = new string('x', 64) }),
            Substitute.For<IEmailService>(),
            Substitute.For<IInvitationManager>(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthManager>.Instance);
    }

    [Fact]
    public async Task The_server_name_given_at_setup_becomes_the_server_name()
    {
        await _manager.ClaimServerAsync("andy@example.com", "a-long-password", "Andy", "  AX Media Server  ");

        _settings.ServerName.Should().Be("AX Media Server");
        await _settingsRepo.Received(1).SaveChangesAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task No_server_name_keeps_the_default(string? serverName)
    {
        await _manager.ClaimServerAsync("andy@example.com", "a-long-password", "Andy", serverName);

        _settings.ServerName.Should().Be("Vora Server");
        await _settingsRepo.DidNotReceive().SaveChangesAsync();
    }
}
