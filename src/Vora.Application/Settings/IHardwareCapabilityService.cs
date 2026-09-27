namespace Vora.Application.Settings;

public interface IHardwareCapabilityService
{
    IReadOnlyList<string> GetAvailableTranscodingDevices();
}
