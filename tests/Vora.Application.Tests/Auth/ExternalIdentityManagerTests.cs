using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth.External;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Auth;

public class ExternalIdentityManagerTests
{
    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly ServerSetting _serverSettings = new();
    private readonly ExternalIdentityManager _manager;
    private readonly User _user = new() { Email = "sam@example.com", DisplayName = "Sam" };

    public ExternalIdentityManagerTests()
    {
        _manager = new ExternalIdentityManager(_providers, _users, _settings, NullLogger<ExternalIdentityManager>.Instance);
        _settings.GetSettingsAsync().Returns(_serverSettings);
        _users.GetUserByIdAsync(_user.Id).Returns(_user);
    }

    private UserExternalIdentity Identity(bool providerEnabled = true) => new()
    {
        UserId = _user.Id,
        Subject = Guid.NewGuid().ToString(),
        Email = "sam@example.com",
        Provider = new AuthProvider { Slug = "p" + Guid.NewGuid().ToString("N")[..6], DisplayName = "Authentik", Enabled = providerEnabled }
    };

    private void Has(params UserExternalIdentity[] identities) =>
        _providers.GetIdentitiesForUserAsync(_user.Id).Returns(identities.ToList());

    [Fact]
    public async Task The_list_names_each_linked_sign_in()
    {
        var identity = Identity();
        Has(identity);

        var list = await _manager.ListAsync(_user.Id);

        list.Should().ContainSingle().Which.ProviderName.Should().Be("Authentik");
    }

    [Fact]
    public async Task An_account_with_a_password_can_unlink_its_only_provider()
    {
        _user.HasPassword = true;
        var identity = Identity();
        Has(identity);

        (await _manager.UnlinkAsync(_user.Id, identity.Id)).Should().Be(UnlinkOutcome.Unlinked);
        await _providers.Received(1).DeleteIdentityAsync(identity.Id);
    }

    [Fact]
    public async Task An_account_without_a_password_cannot_unlink_its_only_way_in()
    {
        _user.HasPassword = false;
        var identity = Identity();
        Has(identity);

        (await _manager.UnlinkAsync(_user.Id, identity.Id)).Should().Be(UnlinkOutcome.LastWayIn);
        await _providers.DidNotReceive().DeleteIdentityAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task An_account_without_a_password_can_unlink_one_of_two_providers()
    {
        _user.HasPassword = false;
        var first = Identity();
        var second = Identity();
        Has(first, second);

        (await _manager.UnlinkAsync(_user.Id, first.Id)).Should().Be(UnlinkOutcome.Unlinked);
    }

    [Fact]
    public async Task A_turned_off_provider_does_not_count_as_another_way_in()
    {
        _user.HasPassword = false;
        var first = Identity();
        var off = Identity(providerEnabled: false);
        Has(first, off);

        (await _manager.UnlinkAsync(_user.Id, first.Id)).Should().Be(UnlinkOutcome.LastWayIn);
    }

    [Fact]
    public async Task A_password_does_not_count_when_password_sign_in_is_off_for_that_person()
    {
        _serverSettings.PasswordSignInEnabled = false;
        _user.HasPassword = true;
        _user.IsAdmin = false;
        var identity = Identity();
        Has(identity);

        (await _manager.UnlinkAsync(_user.Id, identity.Id)).Should().Be(UnlinkOutcome.LastWayIn);
    }

    [Fact]
    public async Task An_administrators_password_still_counts_when_password_sign_in_is_off()
    {
        _serverSettings.PasswordSignInEnabled = false;
        _user.HasPassword = true;
        _user.IsAdmin = true;
        var identity = Identity();
        Has(identity);

        (await _manager.UnlinkAsync(_user.Id, identity.Id)).Should().Be(UnlinkOutcome.Unlinked);
    }

    [Fact]
    public async Task Someone_elses_link_is_not_found()
    {
        Has(Identity());

        (await _manager.UnlinkAsync(_user.Id, Guid.NewGuid())).Should().Be(UnlinkOutcome.NotFound);
    }
}
