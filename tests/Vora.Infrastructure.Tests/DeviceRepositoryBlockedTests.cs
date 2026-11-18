using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class DeviceRepositoryBlockedTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("device-blocked-" + Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task A_blocked_device_is_reported_blocked_and_others_are_not()
    {
        using var db = NewContext();
        db.ClientDevices.Add(new ClientDevice { DeviceId = "tv-blocked", IsBlocked = true });
        db.ClientDevices.Add(new ClientDevice { DeviceId = "tv-fine", IsBlocked = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var repository = new DeviceRepository(db);

        Assert.True(await repository.IsDeviceIdBlockedAsync("tv-blocked"));
        Assert.False(await repository.IsDeviceIdBlockedAsync("tv-fine"));
        Assert.False(await repository.IsDeviceIdBlockedAsync("never-seen"));
    }
}
