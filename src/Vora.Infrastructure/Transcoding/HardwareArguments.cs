using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Transcoding;

internal static class HardwareArguments
{
    public static List<string> InitDevices(HardwareAccelerator hardware, bool withVulkan = false)
    {
        if (hardware.Type == HardwareAccelerationType.Nvidia || string.IsNullOrEmpty(hardware.Device)) return [];

        var args = new List<string> { "-init_hw_device", $"vaapi=va:{hardware.Device}" };
        if (hardware.Type == HardwareAccelerationType.QuickSync) args.AddRange(["-init_hw_device", "qsv=qs@va"]);
        if (withVulkan) args.AddRange(["-init_hw_device", "vulkan=vk@va"]);
        args.AddRange(["-filter_hw_device", "va"]);
        return args;
    }

    public static List<string> Decode(HardwareAccelerator hardware, bool keepFramesOnGpu)
    {
        if (hardware.Type == HardwareAccelerationType.Nvidia)
        {
            var cuda = new List<string> { "-hwaccel", "cuda" };
            if (keepFramesOnGpu) cuda.AddRange(["-hwaccel_output_format", "cuda"]);
            if (hardware.Device is { } device) cuda.AddRange(["-hwaccel_device", device]);
            return cuda;
        }

        var vaapi = new List<string> { "-hwaccel", "vaapi" };
        if (keepFramesOnGpu) vaapi.AddRange(["-hwaccel_output_format", "vaapi"]);
        return vaapi;
    }

    public static List<string> InitAndDecode(HardwareAccelerator hardware, bool keepFramesOnGpu) =>
        [.. InitDevices(hardware), .. Decode(hardware, keepFramesOnGpu)];

    public static string DownloadScaled(HardwareAccelerator hardware, int height) =>
        hardware.Type == HardwareAccelerationType.Nvidia
            ? $"scale_cuda=-2:{height}:format=nv12,hwdownload,format=nv12"
            : $"scale_vaapi=w=-2:h={height}:format=nv12,hwdownload,format=nv12";

    public static string VulkanTonemap(string algorithm, int scaleHeight)
    {
        var size = scaleHeight > 0 ? $"w=-2:h={scaleHeight}:" : string.Empty;
        return "hwmap=derive_device=vulkan,format=vulkan," +
            $"libplacebo={size}format=nv12:tonemapping={algorithm}:colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv," +
            "hwmap=derive_device=vaapi,format=vaapi,scale_vaapi=format=nv12";
    }

    public static string Label(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.Nvidia => "NVIDIA (NVENC)",
        HardwareAccelerationType.QuickSync => "Intel Quick Sync",
        HardwareAccelerationType.Vaapi => "VAAPI",
        HardwareAccelerationType.Off => "Off",
        _ => "Auto"
    };
}
