using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Vora.Infrastructure.Processes;

namespace Vora.Infrastructure.Transcoding;

internal static class SourceDuration
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    public static async Task<double?> ProbeAsync(string filePath, ILogger logger, CancellationToken cancellationToken = default)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nokey=1:noprint_wrappers=1", filePath })
        {
            processInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(processInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            if (!await process.WaitForExitWithTimeoutAsync(ProbeTimeout, logger, cancellationToken)) return null;
            return Read(output);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not measure the length of {FilePath}.", filePath);
            return null;
        }
    }

    internal static double? Read(string? output) =>
        double.TryParse(output?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 && double.IsFinite(seconds)
            ? seconds
            : null;
}
