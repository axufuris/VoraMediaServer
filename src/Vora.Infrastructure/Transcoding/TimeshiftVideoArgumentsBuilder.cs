using System.Globalization;
using Vora.Application.Iptv;
using Vora.Application.Settings;
using Vora.Domain.Enums;

namespace Vora.Infrastructure.Transcoding;

internal static class TimeshiftVideoArgumentsBuilder
{
    public const string KeyframeEveryFourSeconds = "expr:gte(t,n_forced*4)";
    public const string CpuEncoder = "libx264";

    public static TimeshiftVideoArguments Build(LiveVideoEncode encode, HardwareAccelerator? encoder)
    {
        var gpu = encoder is { Type: HardwareAccelerationType.Nvidia or HardwareAccelerationType.QuickSync or HardwareAccelerationType.Vaapi }
            ? encoder
            : null;

        var filters = new List<string>();
        if (encode.Deinterlace) filters.Add("yadif");
        if (encode.MaxHeight is { } height) filters.Add($"scale=-2:'min(ih,{height.ToString(CultureInfo.InvariantCulture)})'");
        filters.AddRange(UploadFilters(gpu));

        var video = new List<string>();
        if (filters.Count > 0) video.AddRange(["-vf", string.Join(",", filters)]);
        video.AddRange(Encoder(gpu));
        video.AddRange(["-profile:v", "high", "-level", encode.Level.ToString(CultureInfo.InvariantCulture)]);
        if (gpu is null or { Type: HardwareAccelerationType.Nvidia }) video.AddRange(["-pix_fmt", "yuv420p"]);
        video.AddRange(["-force_key_frames", KeyframeEveryFourSeconds]);

        return gpu is null
            ? new TimeshiftVideoArguments([], video, CpuEncoder, OnGpu: false)
            : new TimeshiftVideoArguments(HardwareArguments.InitDevices(gpu), video, HardwareArguments.Label(gpu.Type), OnGpu: true);
    }

    private static List<string> Encoder(HardwareAccelerator? gpu) => gpu?.Type switch
    {
        HardwareAccelerationType.Nvidia when gpu.Device is { } device => ["-c:v", "h264_nvenc", "-gpu", device, "-preset", "p4", "-cq", "28", "-forced-idr", "1"],
        HardwareAccelerationType.Nvidia => ["-c:v", "h264_nvenc", "-preset", "p4", "-cq", "28", "-forced-idr", "1"],
        HardwareAccelerationType.QuickSync => ["-c:v", "h264_qsv", "-preset", "veryfast", "-global_quality", "28", "-forced_idr", "1"],
        HardwareAccelerationType.Vaapi => ["-c:v", "h264_vaapi", "-qp", "28"],
        _ => ["-c:v", CpuEncoder, "-preset", "veryfast", "-crf", "23"],
    };

    private static List<string> UploadFilters(HardwareAccelerator? gpu) => gpu?.Type switch
    {
        HardwareAccelerationType.Vaapi => ["format=nv12", "hwupload"],
        HardwareAccelerationType.QuickSync => ["format=nv12"],
        _ => [],
    };
}
