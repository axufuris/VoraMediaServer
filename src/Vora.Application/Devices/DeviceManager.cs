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
    Task UpdateDeviceCapabilitiesAsync(string deviceId, DeviceCapabilitiesDto dto);
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
        return await _repository.GetProjectedDevicesAsync(d => new ClientDeviceVM
        {
            Id = d.Id,
            DeviceId = d.DeviceId,
            ClientName = d.ClientName,
            DeviceName = d.DeviceName,
            DeviceType = d.DeviceType,
            LastIpAddress = d.LastIpAddress,
            OperatingSystem = d.OperatingSystem,
            Location = d.Location,
            LastUserId = d.LastUserId,
            LastProfileId = d.LastProfileId,
            LastConnectedAt = d.LastConnectedAt,
            IsBlocked = d.IsBlocked
        });
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

    public async Task UpdateDeviceCapabilitiesAsync(string deviceId, DeviceCapabilitiesDto dto)
    {
        var device = new ClientDevice
        {
            DeviceId = deviceId,
            ClientName = "Vora Web",
            DeviceName = "Web Browser",
            DeviceType = "Browser",
            LastConnectedAt = DateTime.UtcNow,
            SupportedVideoCodecs = dto.VideoCodecs,
            SupportedAudioCodecs = dto.AudioCodecs,
            SupportedContainers = dto.Containers,
            MaxAudioChannels = dto.MaxAudioChannels
        };

        await _repository.UpsertDeviceCapabilitiesAsync(device);
    }
}