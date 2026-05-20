using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vora.Application.Podcasts;

public class PodcastFeedRefreshWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan MinAgeBeforeRefresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InterFeedDelay = TimeSpan.FromMilliseconds(50);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PodcastFeedRefreshWorker> _logger;

    public PodcastFeedRefreshWorker(IServiceScopeFactory scopeFactory, ILogger<PodcastFeedRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Podcast Feed Refresh Worker is starting.");

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            do
            {
                await RunTickAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Podcast Feed Refresh Worker is stopping.");
        }
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPodcastRepository>();
            var manager = scope.ServiceProvider.GetRequiredService<IPodcastManager>();

            var threshold = DateTime.UtcNow - MinAgeBeforeRefresh;
            var shows = await repository.GetShowsDueForRefreshAsync(threshold);

            if (shows.Count == 0)
            {
                _logger.LogDebug("Podcast refresh tick: no shows due.");
                return;
            }

            _logger.LogInformation("Podcast refresh tick: refreshing {Count} show(s).", shows.Count);

            var successes = 0;
            var failures = 0;

            foreach (var show in shows)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    await manager.RefreshShowAsync(show.Id);
                    successes++;
                }
                catch (Exception ex)
                {
                    failures++;
                    _logger.LogWarning(ex, "Failed to refresh podcast show {ShowId} ({Title}).", show.Id, show.Title);
                }

                try
                {
                    await Task.Delay(InterFeedDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Podcast refresh tick complete: {Successes} refreshed, {Failures} failed.", successes, failures);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Podcast feed refresh tick crashed.");
        }
    }
}
