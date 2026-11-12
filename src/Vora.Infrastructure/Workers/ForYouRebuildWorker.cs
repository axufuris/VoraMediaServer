using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Recommendations;

namespace Vora.Infrastructure.Workers;

// Builds the For You rows that pages asked for while they showed the stored ones
// (docs/for-you.md). Each rebuild runs in its own scope, asks its provider and
// stores what it found. A rebuild that fails keeps the rows already stored.
public class ForYouRebuildWorker(
    IForYouRebuildQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ForYouRebuildWorker> logger) : BackgroundService
{
    // Two at a time, so one slow AI answer doesn't hold up every other rebuild.
    internal const int Concurrency = 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("For You Rebuild Worker started.");

        try
        {
            await Parallel.ForEachAsync(
                queue.Reader.ReadAllAsync(stoppingToken),
                new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = stoppingToken },
                RebuildAsync);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("For You Rebuild Worker stopping.");
        }
    }

    private async ValueTask RebuildAsync(ForYouRebuild rebuild, CancellationToken stoppingToken)
    {
        // Failing, or finding nothing worth keeping, waits out the cooldown
        // before the next page view may ask again.
        var coolDown = true;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var manager = scope.ServiceProvider.GetRequiredService<IRecommendationManager>();
            var result = await manager.RebuildAsync(rebuild, stoppingToken);
            coolDown = result == ForYouRebuildResult.NothingKept;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            coolDown = false;
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rebuilding the For You rows of {Provider} for profile {Profile} failed; the rows already stored are kept.",
                rebuild.ProviderId, rebuild.ProfileId);
        }
        finally
        {
            queue.Finished(rebuild, coolDown);
        }
    }
}
