using System.Text.Json.Nodes;
using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Users;
using Vora.Application.Users.ViewModels;

namespace Vora.Application.Tests.Users;

public class ProfileDeviceSettingsManagerTests
{
    private static readonly DeviceSettings ServerDefaults = new(20, 1080, 15, 10, true, false, true);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IClientDefaultsManager _defaults = Substitute.For<IClientDefaultsManager>();
    private readonly IClientNotifier _notifier = Substitute.For<IClientNotifier>();
    private readonly Guid _profileId = Guid.NewGuid();
    private readonly ProfileDeviceSettingsManager _manager;
    private IReadOnlyDictionary<string, string>? _written;

    public ProfileDeviceSettingsManagerTests()
    {
        _defaults.GetDeviceDefaultsAsync().Returns(ServerDefaults);
        _users.GetProjectedProfileByIdAsync(_profileId, Arg.Any<System.Linq.Expressions.Expression<Func<Vora.Domain.Entities.Users.UserProfile, Guid?>>>())
            .Returns((Guid?)_profileId);
        _users.GetProfileDevicePlaybackPrefsAsync(_profileId, Arg.Any<string>()).Returns((string?)null);
        _users.GetProfileDevicesAsync(_profileId).Returns(new List<ProfileDeviceRecord>
        {
            new("tv", """{"bitrate":4,"maxAudioChannels":6}""", "Living room", "Big screen", "Android TV", "TV", "Android", new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)),
            new("web", null, "", "", "Vora Web", "Browser", "Windows", new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc)),
            new("ghost", """{"seekStepSeconds":30}""", "", "", "", "", "", null)
        });
        _users.SaveProfileDevicePlaybackPrefsAsync(_profileId, Arg.Do<IReadOnlyDictionary<string, string>>(prefs => _written = prefs)).Returns(Task.CompletedTask);
        _manager = new ProfileDeviceSettingsManager(_users, _defaults, _notifier);
    }

    private static SaveProfileDeviceSettingsRequest Request(params string[] deviceIds) => new()
    {
        DeviceIds = deviceIds.Length == 0 ? null : deviceIds.ToList(),
        Settings = new DeviceSettings(40, 2160, 5, 2, false, false, false).ToViewModel()
    };

    [Fact]
    public async Task A_device_that_asks_gets_its_blob_over_the_server_defaults()
    {
        _users.GetProfileDevicePlaybackPrefsAsync(_profileId, "tv").Returns("""{"bitrate":0,"maxAudioChannels":6,"soundOnClick":false}""");

        var playback = await _manager.GetPlaybackAsync(_profileId, "tv");

        playback.Settings.Should().Be(new DeviceSettings(0, 1080, 15, 10, false, false, true));
        playback.MaxAudioChannels.Should().Be(6);
    }

    [Fact]
    public async Task A_device_with_no_row_gets_the_server_defaults()
    {
        (await _manager.GetPlaybackAsync(_profileId, "new-tv")).Settings.Should().Be(ServerDefaults);
    }

    [Fact]
    public async Task Lists_the_profiles_devices_with_what_each_one_actually_uses()
    {
        var devices = await _manager.ListAsync(_profileId);

        devices.Select(device => device.DeviceId).Should().Equal("tv", "web", "ghost");
        devices[0].Name.Should().Be("Living room");
        devices[0].Nickname.Should().Be("Big screen");
        devices[1].Nickname.Should().BeEmpty();
        devices[0].Settings.Bitrate.Should().Be(4);
        devices[0].Settings.MaxResolution.Should().Be(1080);
        devices[1].Name.Should().Be("Vora Web");
        devices[1].Settings.Should().BeEquivalentTo(ServerDefaults.ToViewModel());
        devices[2].Name.Should().Be("ghost");
        devices[2].Settings.SeekStepSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Saving_for_chosen_devices_writes_only_those_and_tells_the_profile_once()
    {
        await _manager.SaveAsync(_profileId, Request("tv"));

        _written.Should().NotBeNull();
        _written!.Keys.Should().Equal("tv");
        var blob = JsonNode.Parse(_written["tv"])!.AsObject();
        blob["bitrate"]!.GetValue<int>().Should().Be(40);
        blob["maxAudioChannels"]!.GetValue<int>().Should().Be(6);
        await _notifier.Received(1).NotifyClientSettingsChangedAsync(_profileId);
    }

    [Fact]
    public async Task Saving_without_a_list_applies_to_every_device_the_profile_has_used()
    {
        await _manager.SaveAsync(_profileId, Request());

        _written!.Keys.Should().BeEquivalentTo("tv", "web", "ghost");
        DevicePlaybackBlob.Parse(_written["web"]).Effective(ServerDefaults).Settings.Bitrate.Should().Be(40);
    }

    [Fact]
    public async Task A_device_the_profile_has_not_used_is_refused_and_nothing_is_written()
    {
        var act = () => _manager.SaveAsync(_profileId, Request("tv", "someone-elses-phone"));

        await act.Should().ThrowAsync<ArgumentException>();
        await _users.DidNotReceive().SaveProfileDevicePlaybackPrefsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>());
        await _notifier.DidNotReceive().NotifyClientSettingsChangedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_setting_out_of_range_is_refused_before_any_device_is_read()
    {
        var request = Request("tv");
        request.Settings.SeekStepSeconds = 7;

        var act = () => _manager.SaveAsync(_profileId, request);

        await act.Should().ThrowAsync<ArgumentException>();
        await _users.DidNotReceive().SaveProfileDevicePlaybackPrefsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task Naming_a_device_twice_writes_it_once()
    {
        await _manager.SaveAsync(_profileId, Request("tv", "tv"));

        _written!.Keys.Should().Equal("tv");
    }

    [Fact]
    public async Task A_profile_with_no_devices_has_nothing_to_write_and_nobody_to_tell()
    {
        _users.GetProfileDevicesAsync(_profileId).Returns(new List<ProfileDeviceRecord>());

        var devices = await _manager.SaveAsync(_profileId, Request());

        devices.Should().BeEmpty();
        await _users.DidNotReceive().SaveProfileDevicePlaybackPrefsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>());
        await _notifier.DidNotReceive().NotifyClientSettingsChangedAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task A_profile_that_does_not_exist_is_not_found()
    {
        var other = Guid.NewGuid();

        var list = () => _manager.ListAsync(other);
        var save = () => _manager.SaveAsync(other, Request());

        await list.Should().ThrowAsync<KeyNotFoundException>();
        await save.Should().ThrowAsync<KeyNotFoundException>();
    }
}
