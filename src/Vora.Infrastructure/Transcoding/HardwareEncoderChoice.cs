using Vora.Application.Settings;
using Vora.Domain.Entities.Settings;

namespace Vora.Infrastructure.Transcoding;

internal static class HardwareEncoderChoice
{
    public static async Task<HardwareAccelerator?> ResolveAsync(ServerSetting settings, IHardwareCapabilityService hardware, CancellationToken cancellationToken = default) =>
        !settings.DisableVideoTranscoding && settings.UseHardwareAcceleration && settings.UseHardwareEncoding
            ? await hardware.ResolveAsync(settings.HardwareAccelerationType, settings.HardwareTranscodingDevice, cancellationToken)
            : null;
}
