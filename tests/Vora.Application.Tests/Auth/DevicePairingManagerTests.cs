using Microsoft.Extensions.Logging.Abstractions;
using Vora.Application.Auth;
using Vora.Application.Auth.Dtos;
using Vora.Application.Auth.External;
using Vora.Application.Auth.Pairing;
using Vora.Application.Devices;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Domain.Entities.Settings;
using Vora.Domain.Entities.Users;
using Vora.Domain.Enums;

namespace Vora.Application.Tests.Auth;

public class DevicePairingManagerTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private readonly ManualClock _clock = new();
    private readonly IAuthManager _auth = Substitute.For<IAuthManager>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISystemSettingsRepository _settings = Substitute.For<ISystemSettingsRepository>();
    private readonly IAuthProviderRepository _providers = Substitute.For<IAuthProviderRepository>();
    private readonly IDeviceRepository _devices = Substitute.For<IDeviceRepository>();
    private readonly IClientLocationResolver _locations = Substitute.For<IClientLocationResolver>();
    private readonly ServerSetting _serverSettings = new();
    private readonly DevicePairingStore _store;
    private readonly DevicePairingManager _manager;
    private readonly User _user = new() { Email = "sam@example.com", DisplayName = "Sam" };

    private static readonly DevicePairingDevice Tv = new("tv-1", "Vora Roku", "Living Room", "TV", "Roku OS", "192.168.0.50");

    public DevicePairingManagerTests()
    {
        _store = new DevicePairingStore(_clock);
        _manager = new DevicePairingManager(_store, _auth, _users, _settings, _providers, _devices, _locations, NullLogger<DevicePairingManager>.Instance);
        _settings.GetSettingsAsync().Returns(_serverSettings);
        _users.GetUserByIdAsync(_user.Id).Returns(_user);
        _providers.GetIdentitiesForUserAsync(_user.Id).Returns(new List<UserExternalIdentity>());
        _locations.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns(ci => ci.ArgAt<string?>(1) ?? "Unknown Location");
        _auth.SignInExternalAsync(_user.Id).Returns(_ => new AuthResponseDto { AccessToken = "account-token", UserId = _user.Id, DisplayName = "Sam", IsAdmin = false });
        _auth.IssueRefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<AuthSignInMethod>()).Returns("refresh-token");
    }

    private static UserExternalIdentity Linked(bool providerEnabled = true) => new()
    {
        Subject = "sub",
        Provider = new AuthProvider { Slug = "authentik", DisplayName = "Authentik", Enabled = providerEnabled }
    };

    private async Task<DevicePairingStartVM> StartAsync(DevicePairingDevice? device = null)
    {
        var result = await _manager.StartAsync(device ?? Tv, "https://vora.example.com");
        result.Outcome.Should().Be(DevicePairingStartOutcome.Started);
        return result.Pairing!;
    }

    [Fact]
    public async Task A_started_pairing_hands_the_device_its_codes_and_where_to_enter_them()
    {
        var pairing = await StartAsync();

        pairing.UserCode.Should().MatchRegex("^[BCDFGHJKMNPQRSTVWXZ2-9]{4}-[BCDFGHJKMNPQRSTVWXZ2-9]{4}$");
        pairing.VerificationUrl.Should().Be("https://vora.example.com/link");
        pairing.VerificationUrlComplete.Should().Be($"https://vora.example.com/link?code={pairing.UserCode.Replace("-", string.Empty)}");
        pairing.ExpiresInSeconds.Should().Be(600);
        pairing.IntervalSeconds.Should().Be(5);
        pairing.DeviceCode.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_address_to_open_is_the_servers_public_address_when_one_is_set()
    {
        _serverSettings.PublicBaseUrl = "https://watch.example.org/";

        var pairing = await StartAsync();

        pairing.VerificationUrl.Should().Be("https://watch.example.org/link");
    }

    [Fact]
    public async Task A_blocked_device_cannot_start_a_pairing()
    {
        _devices.IsDeviceIdBlockedAsync("tv-1").Returns(true);

        var result = await _manager.StartAsync(Tv, "https://vora.example.com");

        result.Outcome.Should().Be(DevicePairingStartOutcome.DeviceBlocked);
        result.Pairing.Should().BeNull();
    }

    [Fact]
    public async Task Looking_up_a_code_shows_the_device_that_asked_for_it()
    {
        var pairing = await StartAsync();

        var result = await _manager.LookupAsync(pairing.UserCode, _user.Id, "192.168.0.7");

        result.Outcome.Should().Be(DevicePairingLookupOutcome.Found);
        result.Request.Should().NotBeNull();
        result.Request!.ClientName.Should().Be("Vora Roku");
        result.Request.DeviceName.Should().Be("Living Room");
        result.Request.OperatingSystem.Should().Be("Roku OS");
        result.Request.UserCode.Should().Be(pairing.UserCode);
        result.Request.SameNetwork.Should().BeTrue();
    }

    [Fact]
    public async Task A_device_on_another_network_is_flagged_so_the_page_can_warn()
    {
        var pairing = await StartAsync(Tv with { IpAddress = "203.0.113.9" });

        var result = await _manager.LookupAsync(pairing.UserCode, _user.Id, "198.51.100.4");

        result.Request!.SameNetwork.Should().BeFalse();
    }

    [Fact]
    public async Task A_code_typed_in_lower_case_without_the_dash_still_finds_the_device()
    {
        var pairing = await StartAsync();

        var result = await _manager.LookupAsync(pairing.UserCode.Replace("-", string.Empty).ToLowerInvariant(), _user.Id, null);

        result.Outcome.Should().Be(DevicePairingLookupOutcome.Found);
    }

    [Fact]
    public async Task Five_codes_that_match_nothing_lock_the_account_out_even_for_the_right_one()
    {
        var pairing = await StartAsync();

        for (var i = 0; i < DevicePairingStore.MaxWrongCodes; i++)
        {
            (await _manager.LookupAsync("BCDFGHJK", _user.Id, null)).Outcome.Should().Be(DevicePairingLookupOutcome.NotFound);
        }

        (await _manager.LookupAsync(pairing.UserCode, _user.Id, null)).Outcome.Should().Be(DevicePairingLookupOutcome.LockedOut);
        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.LockedOut);
    }

    [Fact]
    public async Task Approving_binds_the_session_to_the_account_that_approved_and_the_device_gets_it_once()
    {
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.Approved);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var first = await _manager.PollAsync(pairing.DeviceCode);
        _clock.Advance(TimeSpan.FromSeconds(5));
        var second = await _manager.PollAsync(pairing.DeviceCode);

        first.Status.Should().Be("approved");
        first.Auth!.AccessToken.Should().Be("account-token");
        first.Auth.RefreshToken.Should().Be("refresh-token");
        await _auth.Received(1).SignInExternalAsync(_user.Id);
        await _auth.Received(1).IssueRefreshTokenAsync(_user.Id, "tv-1", AuthSignInMethod.Password);
        second.Status.Should().Be("expired");
        second.Auth.Should().BeNull();
    }

    [Fact]
    public async Task A_denied_pairing_gives_the_device_no_session()
    {
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: false)).Should().Be(DevicePairingDecideOutcome.Denied);
        var polled = await _manager.PollAsync(pairing.DeviceCode);

        polled.Status.Should().Be("denied");
        await _auth.DidNotReceive().SignInExternalAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task Polling_before_an_answer_says_pending_and_a_hurried_poll_slow_down()
    {
        var pairing = await StartAsync();

        var pending = await _manager.PollAsync(pairing.DeviceCode);
        var hurried = await _manager.PollAsync(pairing.DeviceCode);

        pending.Status.Should().Be("pending");
        pending.IntervalSeconds.Should().Be(5);
        hurried.Status.Should().Be("slow_down");
        hurried.IntervalSeconds.Should().Be(10);
    }

    [Fact]
    public async Task A_second_answer_to_the_same_code_is_refused()
    {
        var pairing = await StartAsync();
        await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: false);

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.AlreadyDecided);
    }

    [Fact]
    public async Task A_code_that_matches_nothing_is_not_found_and_counts_against_the_account()
    {
        (await _manager.DecideAsync("BCDFGHJK", _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.NotFound);
        (await _manager.DecideAsync("nonsense", _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.NotFound);
    }

    [Fact]
    public async Task With_password_sign_in_off_a_password_only_account_cannot_approve()
    {
        _serverSettings.PasswordSignInEnabled = false;
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.AccountNotAllowed);
        _store.FindPending(DevicePairingCode.Normalize(pairing.UserCode)!).Should().NotBeNull();
    }

    [Fact]
    public async Task With_password_sign_in_off_an_account_with_a_turned_on_provider_can_approve_and_the_device_session_survives_the_switch()
    {
        _serverSettings.PasswordSignInEnabled = false;
        _providers.GetIdentitiesForUserAsync(_user.Id).Returns(new List<UserExternalIdentity> { Linked() });
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.Approved);
        await _manager.PollAsync(pairing.DeviceCode);

        await _auth.Received(1).IssueRefreshTokenAsync(_user.Id, "tv-1", AuthSignInMethod.External);
    }

    [Fact]
    public async Task A_provider_that_is_turned_off_does_not_count()
    {
        _serverSettings.PasswordSignInEnabled = false;
        _providers.GetIdentitiesForUserAsync(_user.Id).Returns(new List<UserExternalIdentity> { Linked(providerEnabled: false) });
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.AccountNotAllowed);
    }

    [Fact]
    public async Task An_administrator_can_approve_while_password_sign_in_is_off()
    {
        _serverSettings.PasswordSignInEnabled = false;
        _user.IsAdmin = true;
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.Approved);
    }

    [Fact]
    public async Task A_person_who_may_not_approve_can_still_deny()
    {
        _serverSettings.PasswordSignInEnabled = false;
        var pairing = await StartAsync();

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: false)).Should().Be(DevicePairingDecideOutcome.Denied);
    }

    [Fact]
    public async Task A_device_blocked_after_it_asked_cannot_be_approved()
    {
        var pairing = await StartAsync();
        _devices.IsDeviceIdBlockedAsync("tv-1").Returns(true);

        (await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true)).Should().Be(DevicePairingDecideOutcome.DeviceBlocked);
    }

    [Fact]
    public async Task A_device_blocked_between_the_approval_and_the_poll_gets_nothing()
    {
        var pairing = await StartAsync();
        await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true);
        _devices.IsDeviceIdBlockedAsync("tv-1").Returns(true);
        _clock.Advance(TimeSpan.FromSeconds(5));

        var polled = await _manager.PollAsync(pairing.DeviceCode);

        polled.Status.Should().Be("denied");
        await _auth.DidNotReceive().IssueRefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<AuthSignInMethod>());
    }

    [Fact]
    public async Task An_account_removed_after_approving_gets_the_device_nothing()
    {
        var pairing = await StartAsync();
        await _manager.DecideAsync(pairing.UserCode, _user.Id, approve: true);
        _users.GetUserByIdAsync(_user.Id).Returns((User?)null);
        _clock.Advance(TimeSpan.FromSeconds(5));

        (await _manager.PollAsync(pairing.DeviceCode)).Status.Should().Be("denied");
    }
}
