using Microsoft.EntityFrameworkCore;
using Vora.Domain.Entities.Users;
using Vora.Infrastructure.Persistence;
using Vora.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Vora.Infrastructure.Tests;

public class ProfileDeviceSettingsMergeTests
{
    private static VoraDbContext NewContext() =>
        new(new DbContextOptionsBuilder<VoraDbContext>()
            .UseInMemoryDatabase("device-settings-" + Guid.NewGuid().ToString("N"))
            .Options);

    private const string Playback = "{\"maxBitrate\":8000}";
    private const string Iptv = "{\"enabledProviders\":[\"p1\"],\"hiddenChannels\":[\"c9\"]}";

    private static async Task<(VoraDbContext Db, UserRepository Repo, Guid ProfileId, string DeviceId)> SeededAsync()
    {
        var db = NewContext();
        var repo = new UserRepository(db);
        var profileId = Guid.NewGuid();
        const string deviceId = "device-1";
        await repo.SaveProfileDeviceSettingsAsync(profileId, deviceId, Playback, Iptv);
        return (db, repo, profileId, deviceId);
    }

    private static Task<ProfileDeviceSetting?> RowAsync(VoraDbContext db, Guid profileId, string deviceId) =>
        db.Set<ProfileDeviceSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.ProfileId == profileId && s.DeviceId == deviceId);

    [Fact]
    public async Task Saving_only_playback_keeps_the_guide_preferences()
    {
        var (db, repo, profileId, deviceId) = await SeededAsync();
        await using var _ = db;

        await repo.SaveProfileDeviceSettingsAsync(profileId, deviceId, "{\"maxBitrate\":4000}", null);

        var row = await RowAsync(db, profileId, deviceId);
        row!.PlaybackPrefs.Should().Be("{\"maxBitrate\":4000}");
        row.IptvPrefsJson.Should().Be(Iptv);
    }

    [Fact]
    public async Task Saving_only_the_guide_preferences_keeps_playback()
    {
        var (db, repo, profileId, deviceId) = await SeededAsync();
        await using var _ = db;

        await repo.SaveProfileDeviceSettingsAsync(profileId, deviceId, null, "{\"enabledProviders\":[]}");

        var row = await RowAsync(db, profileId, deviceId);
        row!.PlaybackPrefs.Should().Be(Playback);
        row.IptvPrefsJson.Should().Be("{\"enabledProviders\":[]}");
    }

    [Fact]
    public async Task An_explicit_empty_object_still_clears_the_guide_preferences()
    {
        var (db, repo, profileId, deviceId) = await SeededAsync();
        await using var _ = db;

        await repo.SaveProfileDeviceSettingsAsync(profileId, deviceId, null, "{}");

        var row = await RowAsync(db, profileId, deviceId);
        row!.IptvPrefsJson.Should().Be("{}");
    }
}
