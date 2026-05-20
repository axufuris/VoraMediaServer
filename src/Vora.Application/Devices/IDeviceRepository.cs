using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Devices;

public interface IDeviceRepository
{
    Task<List<T>> GetProjectedDevicesAsync<T>(Expression<Func<ClientDevice, T>> projection);
    Task<ClientDevice?> GetDeviceByIdAsync(Guid id);
    Task<ClientDevice?> GetDeviceByDeviceIdAsync(string deviceId);
    Task UpdateDeviceAsync(ClientDevice device);
    Task UpsertDeviceCapabilitiesAsync(ClientDevice device);
    Task DeleteDeviceAsync(ClientDevice device);
    Task AddDeviceAsync(ClientDevice device);
}
