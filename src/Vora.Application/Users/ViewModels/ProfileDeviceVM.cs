namespace Vora.Application.Users.ViewModels;

public class ProfileDeviceVM
{
    public required string DeviceId { get; set; }
    public required string Name { get; set; }
    public required string Nickname { get; set; }
    public required string ClientName { get; set; }
    public required string DeviceType { get; set; }
    public required string OperatingSystem { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public required DeviceSettingsVM Settings { get; set; }
}
