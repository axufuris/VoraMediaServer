namespace Vora.Application.Users.ViewModels;

public class ClientDefaultsVM : ClientSettingsVM
{
    public required DeviceSettingsVM Device { get; set; }
}

public class SaveClientDefaultsRequest : SaveClientSettingsRequest
{
    public DeviceSettingsVM? Device { get; set; }
}
