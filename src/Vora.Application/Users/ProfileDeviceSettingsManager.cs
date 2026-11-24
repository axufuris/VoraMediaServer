using Vora.Application.Analysis;
using Vora.Application.Settings;
using Vora.Application.Users.ViewModels;

namespace Vora.Application.Users;

public interface IProfileDeviceSettingsManager
{
    Task<EffectiveDevicePlayback> GetPlaybackAsync(Guid profileId, string deviceId);
    Task<List<ProfileDeviceVM>> ListAsync(Guid profileId);
    Task<List<ProfileDeviceVM>> SaveAsync(Guid profileId, SaveProfileDeviceSettingsRequest request);
}

public sealed class ProfileDeviceSettingsManager(IUserRepository users, IClientDefaultsManager defaults, IClientNotifier notifier) : IProfileDeviceSettingsManager
{
    public async Task<EffectiveDevicePlayback> GetPlaybackAsync(Guid profileId, string deviceId) =>
        DevicePlaybackBlob.Parse(await users.GetProfileDevicePlaybackPrefsAsync(profileId, deviceId))
            .Effective(await defaults.GetDeviceDefaultsAsync());

    public async Task<List<ProfileDeviceVM>> ListAsync(Guid profileId)
    {
        await EnsureProfileAsync(profileId);
        return await ViewOfAsync(profileId);
    }

    public async Task<List<ProfileDeviceVM>> SaveAsync(Guid profileId, SaveProfileDeviceSettingsRequest request)
    {
        await EnsureProfileAsync(profileId);
        var settings = DeviceSettings.From(request.Settings);

        var devices = await users.GetProfileDevicesAsync(profileId);
        var targets = request.DeviceIds is null
            ? devices
            : request.DeviceIds.Distinct().Select(id => devices.FirstOrDefault(device => device.DeviceId == id)
                ?? throw new ArgumentException($"'{id}' is not a device this profile has used.", nameof(request))).ToList();

        var blobs = targets.ToDictionary(
            device => device.DeviceId,
            device => DevicePlaybackBlob.Parse(device.PlaybackPrefs).With(settings).ToJson());

        if (blobs.Count > 0)
        {
            await users.SaveProfileDevicePlaybackPrefsAsync(profileId, blobs);
            await notifier.NotifyClientSettingsChangedAsync(profileId);
        }

        return await ViewOfAsync(profileId);
    }

    private async Task EnsureProfileAsync(Guid profileId)
    {
        if (await users.GetProjectedProfileByIdAsync(profileId, profile => (Guid?)profile.Id) is null)
        {
            throw new KeyNotFoundException("Profile not found.");
        }
    }

    private async Task<List<ProfileDeviceVM>> ViewOfAsync(Guid profileId)
    {
        var deviceDefaults = await defaults.GetDeviceDefaultsAsync();
        return (await users.GetProfileDevicesAsync(profileId))
            .Select(device => new ProfileDeviceVM
            {
                DeviceId = device.DeviceId,
                Name = FirstNamed(device.DeviceName, device.ClientName, device.DeviceId),
                ClientName = device.ClientName,
                DeviceType = device.DeviceType,
                OperatingSystem = device.OperatingSystem,
                LastConnectedAt = device.LastConnectedAt,
                Settings = DevicePlaybackBlob.Parse(device.PlaybackPrefs).Effective(deviceDefaults).Settings.ToViewModel()
            })
            .ToList();
    }

    private static string FirstNamed(params string[] names) =>
        names.First(name => !string.IsNullOrWhiteSpace(name));
}
