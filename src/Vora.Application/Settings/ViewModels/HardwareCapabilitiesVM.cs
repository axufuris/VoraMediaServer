using Vora.Domain.Enums;

namespace Vora.Application.Settings.ViewModels;

public class HardwareCapabilitiesVM
{
    public List<HardwareDeviceVM> Devices { get; set; } = new();
    public HardwareAccelerationType? Detected { get; set; }
    public string? DetectedDevice { get; set; }
}

public class HardwareDeviceVM
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GpuVendor Vendor { get; set; }
}

public class HardwareTestRequest
{
    public HardwareAccelerationType Type { get; set; }
    public string Device { get; set; } = "Auto";
}

public class HardwareTestResultVM
{
    public bool Success { get; set; }
    public HardwareAccelerationType? Type { get; set; }
    public string? Device { get; set; }
    public string Message { get; set; } = string.Empty;
}
