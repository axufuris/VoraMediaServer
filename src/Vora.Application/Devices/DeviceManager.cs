using Microsoft.Extensions.Caching.Memory;
using Vora.Application.Devices.Dtos;
using Vora.Application.Devices.ViewModels;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Devices;

public interface IDeviceManager
{
    Task<List<ClientDeviceVM>> GetAllDevicesAsync();
    Task<bool> BlockDeviceAsync(Guid id);
    Task<bool> UnblockDeviceAsync(Guid id);
    Task<bool> DeleteDeviceAsync(Guid id);
    Task<bool> SetNicknameAsync(Guid id, string? nickname);
    Task UpdateDeviceCapabilitiesAsync(string deviceId, DeviceIdentity identity, DeviceCapabilitiesDto dto);
}

public class DeviceManager : IDeviceManager
{
    private readonly IDeviceRepository _repository;
    private readonly IMemoryCache _cache;

    public DeviceManager(IDeviceRepository repository, IMemoryCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<List<ClientDeviceVM>> GetAllDevicesAsync()
    {
        return await _repository.GetProjectedDevicesAsync(ClientDeviceVM.Projection);
    }

    public async Task<bool> BlockDeviceAsync(Guid id)
    {
        var device = await _repository.GetDeviceByIdAsync(id);
        if (device == null) return false;

        device.IsBlocked = true;
        await _repository.UpdateDeviceAsync(device);

        _cache.Set($"BlockedDevice_{device.DeviceId}", true, TimeSpan.FromMinutes(5));
        return true;
    }

    public async Task<bool> UnblockDeviceAsync(Guid id)
    {
        var device = await _repository.GetDeviceByIdAsync(id);
        if (device == null) return false;

        device.IsBlocked = false;
        await _repository.UpdateDeviceAsync(device);

        _cache.Remove($"BlockedDevice_{device.DeviceId}");
        return true;
    }

    public async Task<bool> DeleteDeviceAsync(Guid id)
    {
        var device = await _repository.GetDeviceByIdAsync(id);
        if (device != null)
        {
            _cache.Remove($"BlockedDevice_{device.DeviceId}");
            _cache.Remove($"DeviceTrack_{device.DeviceId}");
            await _repository.DeleteDeviceAsync(device);
            return true;
        }
        return false;
    }

    public async Task<bool> SetNicknameAsync(Guid id, string? nickname)
    {
        var normalized = DeviceNickname.Normalize(nickname);

        var device = await _repository.GetDeviceByIdAsync(id);
        if (device == null) return false;

        device.Nickname = normalized;
        await _repository.UpdateDeviceAsync(device);
        return true;
    }

    public async Task UpdateDeviceCapabilitiesAsync(string deviceId, DeviceIdentity identity, DeviceCapabilitiesDto dto)
    {
        var device = new ClientDevice
        {
            DeviceId = deviceId,
            ClientName = identity.ClientName ?? DeviceIdentity.UnknownClient,
            DeviceName = identity.DeviceName ?? DeviceIdentity.UnknownDevice,
            DeviceType = identity.DeviceType ?? DeviceIdentity.UnknownType,
            OperatingSystem = identity.OperatingSystem ?? DeviceIdentity.UnknownOperatingSystem,
            LastConnectedAt = DateTime.UtcNow,
            SupportedVideoCodecs = dto.VideoCodecs,
            SupportedAudioCodecs = dto.AudioCodecs,
            SupportedContainers = dto.Containers,
            MaxAudioChannels = dto.MaxAudioChannels,
            SupportedHdrFormats = dto.SupportedHdrFormats,
            MaxVideoBitDepth = dto.MaxVideoBitDepth,
            RendersImageSubtitles = dto.RendersImageSubtitles
        };

        await _repository.UpsertDeviceCapabilitiesAsync(device);
    }
}