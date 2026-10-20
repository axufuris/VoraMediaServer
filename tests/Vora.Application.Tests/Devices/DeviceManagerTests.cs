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
}
