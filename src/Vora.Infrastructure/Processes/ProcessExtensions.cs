using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Vora.Infrastructure.Processes;

public static class ProcessExtensions
{
    public static async Task<bool> WaitForExitWithTimeoutAsync(this Process process, TimeSpan timeout, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            var processName = process.StartInfo.FileName;
            logger?.LogWarning("Process {ProcessName} exceeded timeout {TimeoutSeconds}s or was cancelled; killing process tree.", processName, timeout.TotalSeconds);
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception killEx)
            {
                logger?.LogWarning(killEx, "Failed to kill process {ProcessName} after timeout.", processName);
            }
            return false;
        }
    }
}
