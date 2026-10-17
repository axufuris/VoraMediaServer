using System.ComponentModel;
using System.Diagnostics;
using Vora.Application.Settings;
using Vora.Domain.Enums;
using Vora.Infrastructure.Processes;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Settings;

internal sealed record HardwareProbe(HardwareAccelerationType Type, string? Device, bool VulkanTonemap = false, bool Hdr10Bit = false)
{
    public const string TestClip = "testsrc2=size=1280x720:rate=30";

    public List<string> Arguments()
    {
        var hardware = new HardwareAccelerator(Type, Device, GpuVendor.Unknown);
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error" };
        args.AddRange(HardwareArguments.InitDevices(hardware, withVulkan: VulkanTonemap));
        args.AddRange(["-f", "lavfi", "-i", TestClip, "-t", "2"]);
        args.AddRange(Hdr10Bit ? HdrEncode() : Type switch
        {
            HardwareAccelerationType.Nvidia when Device is { } gpu => ["-c:v", "h264_nvenc", "-gpu", gpu],
            HardwareAccelerationType.Nvidia => ["-c:v", "h264_nvenc"],
            HardwareAccelerationType.QuickSync => ["-vf", "format=nv12,hwupload,hwmap=derive_device=qsv,format=qsv", "-c:v", "h264_qsv"],
            _ when VulkanTonemap => ["-vf", $"format=p010le,hwupload,{HardwareArguments.VulkanTonemap("hable")}", "-c:v", "h264_vaapi"],
            _ => ["-vf", "format=nv12,hwupload", "-c:v", "h264_vaapi"]
        });
        args.AddRange(["-f", "null", "-"]);
        return args;
    }

    private List<string> HdrEncode()
    {
        var encode = Type switch
        {
            HardwareAccelerationType.Nvidia => new List<string> { "-vf", "format=p010le", "-c:v", "hevc_nvenc" },
            HardwareAccelerationType.QuickSync => ["-vf", "format=p010le,hwupload,hwmap=derive_device=qsv,format=qsv", "-c:v", "hevc_qsv"],
            _ => ["-vf", "format=p010le,hwupload", "-c:v", "hevc_vaapi"]
        };
        encode.AddRange(["-profile:v", "main10"]);
        if (Type == HardwareAccelerationType.Nvidia && Device is { } gpu) encode.AddRange(["-gpu", gpu]);
        return encode;
    }
}

internal sealed record ProbeOutcome(bool Success, string Detail, TimeSpan Elapsed)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task<ProbeOutcome> RunFfmpegAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return new(false, "FFmpeg could not be started.", stopwatch.Elapsed);

            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var exited = await process.WaitForExitWithTimeoutAsync(Timeout, cancellationToken: cancellationToken);
            var errors = await stderr;
            if (!exited) return new(false, $"FFmpeg didn't finish the test within {Timeout.TotalSeconds:0} seconds.", stopwatch.Elapsed);
            return new(process.ExitCode == 0, LastLines(errors), stopwatch.Elapsed);
        }
        catch (Win32Exception)
        {
            return new(false, "FFmpeg isn't installed.", stopwatch.Elapsed);
        }
    }

    internal static string LastLines(string stderr) =>
        string.Join(" ", stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(3));
}
