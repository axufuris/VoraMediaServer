using System.Linq.Expressions;
using Vora.Domain.Entities.Users;

namespace Vora.Application.Devices.ViewModels;

public class ClientDeviceVM
{
    public Guid Id { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string LastIpAddress { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public Guid? LastUserId { get; set; }
    public Guid? LastProfileId { get; set; }
    public DateTime LastConnectedAt { get; set; }
    public bool IsBlocked { get; set; }

    public static Expression<Func<ClientDevice, ClientDeviceVM>> Projection =>
        d => new ClientDeviceVM
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
        };
}
