using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Streaming;

namespace Vora.Infrastructure.Workers;

public class TranscodeThrottleWorker(ITranscodeService transcodeService, ILogger<TranscodeThrottleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await transcodeService.ThrottleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Error while throttling transcodes.");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
