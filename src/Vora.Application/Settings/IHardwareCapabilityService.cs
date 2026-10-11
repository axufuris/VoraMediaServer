using Vora.Application.Settings.ViewModels;
using Vora.Domain.Enums;

namespace Vora.Application.Settings;

public interface IHardwareCapabilityService
{
    Task<HardwareCapabilitiesVM> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<HardwareTestResultVM> TestAsync(HardwareAccelerationType type, string device, CancellationToken cancellationToken = default);
    Task<HardwareAccelerator?> ResolveAsync(HardwareAccelerationType type, string device, CancellationToken cancellationToken = default);
}
