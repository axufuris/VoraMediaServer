using Vora.Domain.Enums;

namespace Vora.Application.Settings;

public sealed record HardwareAccelerator(HardwareAccelerationType Type, string? Device, GpuVendor Vendor, bool HasVulkanInterop = false)
{
    public bool TonemapsOnGpu => Type == HardwareAccelerationType.Nvidia || Vendor == GpuVendor.Intel || HasVulkanInterop;
}
