namespace Vora.Application.Users.ViewModels;

public class SaveProfileDeviceSettingsRequest
{
    public List<string>? DeviceIds { get; set; }
    public required DeviceSettingsVM Settings { get; set; }
}
