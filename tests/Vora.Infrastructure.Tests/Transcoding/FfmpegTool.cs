using System.ComponentModel;
using System.Diagnostics;

namespace Vora.Infrastructure.Tests.Transcoding;

internal static class FfmpegTool
{
    public const string SkipReason = "Needs ffmpeg with libx264 on PATH.";

    public static bool IsAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null) return false;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public const string JellyfinSkipReason = "Needs jellyfin-ffmpeg on PATH, whose command line pauses on p and resumes on u.";

    public static bool IsJellyfinBuild()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-hide_banner -version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null) return false;
            var version = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && version.Contains("Jellyfin", StringComparison.OrdinalIgnoreCase);
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static async Task<byte[]> RunAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg did not start");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        var errors = await process.StandardError.ReadToEndAsync(cancellationToken);
        await copy;
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffmpeg {string.Join(" ", arguments)} failed: {errors}");
        return output.ToArray();
    }

    public static async Task<int> BrightPixelsAsync(string video, int frameNumber, CancellationToken cancellationToken)
    {
        var frame = await RunAsync(cancellationToken,
            "-i", video, "-vf", $"select='eq(n,{frameNumber})',scale=32:24:flags=area,format=gray", "-frames:v", "1", "-f", "rawvideo", "-");
        frame.Should().NotBeEmpty();
        return frame.Count(value => value > 100);
    }
}
