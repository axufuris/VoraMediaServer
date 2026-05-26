using System.Runtime.InteropServices;
using Vora.Application.Settings;

namespace Vora.Infrastructure.Settings;

public class HardwareCapabilityService : IHardwareCapabilityService
{
    public IReadOnlyList<string> GetAvailableTranscodingDevices()
    {
        var devices = new List<string> { "Auto" };

        if (Directory.Exists("/dev/dri"))
        {
            devices.AddRange(Directory.GetFiles("/dev/dri", "renderD*"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            devices.Add("0");
            devices.Add("1");
        }

        return devices;
    }
}
