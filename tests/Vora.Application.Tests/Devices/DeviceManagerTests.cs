using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Devices;
using Vora.Application.Devices.Dtos;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Tests.Devices;

public class DeviceManagerTests
{
    private readonly IDeviceRepository _repository = Substitute.For<IDeviceRepository>();
    private ClientDevice? _saved;

    public DeviceManagerTests()
    {
        _repository.UpsertDeviceCapabilitiesAsync(Arg.Do<ClientDevice>(device => _saved = device)).Returns(Task.CompletedTask);
    }

    private DeviceManager Manager() => new(_repository, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Capabilities_name_the_device_as_the_client_does_rather_than_as_a_web_browser()
    {
        var desktop = new DeviceIdentity("Vora Desktop QA", "Vora Desktop QA (Windows)", "Desktop", "Windows");

        await Manager().UpdateDeviceCapabilitiesAsync("desktop-1", desktop, new DeviceCapabilitiesDto { VideoCodecs = ["hevc"], RendersImageSubtitles = true });

        _saved.Should().BeEquivalentTo(new
        {
            DeviceId = "desktop-1",
            ClientName = "Vora Desktop QA",
            DeviceName = "Vora Desktop QA (Windows)",
            DeviceType = "Desktop",
            OperatingSystem = "Windows",
            SupportedVideoCodecs = new List<string> { "hevc" },
            RendersImageSubtitles = (bool?)true,
        });
    }

    [Fact]
    public async Task Capabilities_from_a_client_that_sends_no_names_use_the_names_device_tracking_uses()
    {
        await Manager().UpdateDeviceCapabilitiesAsync("app-1", new DeviceIdentity(null, null, null, null), new DeviceCapabilitiesDto());

        _saved.Should().BeEquivalentTo(new
        {
            ClientName = DeviceIdentity.UnknownClient,
            DeviceName = DeviceIdentity.UnknownDevice,
            DeviceType = DeviceIdentity.UnknownType,
            OperatingSystem = DeviceIdentity.UnknownOperatingSystem,
        });
    }

    [Fact]
    public async Task A_nickname_is_saved_tidied_on_the_device_it_names()
    {
        var device = new ClientDevice { DeviceId = "tv-1", DeviceName = "Shield" };
        _repository.GetDeviceByIdAsync(device.Id).Returns(device);

        (await Manager().SetNicknameAsync(device.Id, "  Living   room\tTV ")).Should().BeTrue();

        device.Nickname.Should().Be("Living room TV");
        device.DeviceName.Should().Be("Shield");
        await _repository.Received(1).UpdateDeviceAsync(device);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_nickname_takes_the_nickname_off(string? nickname)
    {
        var device = new ClientDevice { DeviceId = "tv-1", Nickname = "Living room" };
        _repository.GetDeviceByIdAsync(device.Id).Returns(device);

        (await Manager().SetNicknameAsync(device.Id, nickname)).Should().BeTrue();

        device.Nickname.Should().BeEmpty();
        await _repository.Received(1).UpdateDeviceAsync(device);
    }

    [Fact]
    public async Task A_nickname_for_a_device_that_is_not_there_changes_nothing()
    {
        (await Manager().SetNicknameAsync(Guid.NewGuid(), "Kitchen")).Should().BeFalse();

        await _repository.DidNotReceive().UpdateDeviceAsync(Arg.Any<ClientDevice>());
    }

    [Fact]
    public async Task A_nickname_that_is_not_valid_is_refused_before_the_device_is_read()
    {
        var device = new ClientDevice { DeviceId = "tv-1", Nickname = "Living room" };
        _repository.GetDeviceByIdAsync(device.Id).Returns(device);

        var act = () => Manager().SetNicknameAsync(device.Id, new string('x', DeviceNickname.MaxLength + 1));

        await act.Should().ThrowAsync<ArgumentException>();
        device.Nickname.Should().Be("Living room");
        await _repository.DidNotReceive().GetDeviceByIdAsync(Arg.Any<Guid>());
        await _repository.DidNotReceive().UpdateDeviceAsync(Arg.Any<ClientDevice>());
    }

    [Fact]
    public async Task The_device_list_carries_each_nickname()
    {
        _repository.GetProjectedDevicesAsync(Arg.Any<System.Linq.Expressions.Expression<Func<ClientDevice, Vora.Application.Devices.ViewModels.ClientDeviceVM>>>())
            .Returns(call => new List<ClientDevice> { new() { DeviceId = "tv-1", Nickname = "Living room" } }
                .Select(call.Arg<System.Linq.Expressions.Expression<Func<ClientDevice, Vora.Application.Devices.ViewModels.ClientDeviceVM>>>().Compile()).ToList());

        var devices = await Manager().GetAllDevicesAsync();

        devices.Single().Nickname.Should().Be("Living room");
    }
}
