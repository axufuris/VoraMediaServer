using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class DeviceRepositoryNicknameTests
{
    private static VoraDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    [Fact]
    public async Task A_device_reporting_its_capabilities_keeps_the_nickname_an_admin_gave_it()
    {
        var name = "device-nickname-" + Guid.NewGuid().ToString("N");
        await using (var seed = NewContext(name))
        {
            seed.ClientDevices.Add(new ClientDevice { DeviceId = "tv-1", DeviceName = "Shield", Nickname = "Living room" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var db = NewContext(name))
        {
            await new DeviceRepository(db).UpsertDeviceCapabilitiesAsync(new ClientDevice
            {
                DeviceId = "tv-1",
                DeviceName = "Shield TV Pro",
                MaxAudioChannels = 8,
                SupportedVideoCodecs = ["hevc"],
            });
        }

        await using var check = NewContext(name);
        var device = await check.ClientDevices.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        device.Nickname.Should().Be("Living room");
        device.MaxAudioChannels.Should().Be(8);
    }

    [Fact]
    public async Task A_device_first_seen_through_its_capabilities_has_no_nickname()
    {
        await using var db = NewContext("device-nickname-" + Guid.NewGuid().ToString("N"));

        await new DeviceRepository(db).UpsertDeviceCapabilitiesAsync(new ClientDevice { DeviceId = "new-tv", DeviceName = "Shield" });

        (await db.ClientDevices.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).Nickname.Should().BeEmpty();
    }
}
