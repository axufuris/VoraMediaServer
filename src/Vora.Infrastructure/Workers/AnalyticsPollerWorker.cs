using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Vora.Application.Admin;
using Vora.Application.Analysis;
using Vora.Domain.Entities.Tracking;

namespace Vora.Infrastructure.Workers;

public class AnalyticsPollerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnalyticsPollerWorker> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromMinutes(5);

    private TimeSpan _lastTotalProcessorTime;
    private DateTime _lastMonitorTime;

    public AnalyticsPollerWorker(IServiceScopeFactory scopeFactory, ILogger<AnalyticsPollerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analytics Poller Service has started with System Diagnostics.");

        using var currentProcess = Process.GetCurrentProcess();
        _lastTotalProcessorTime = currentProcess.TotalProcessorTime;
        _lastMonitorTime = DateTime.UtcNow;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(_pollingInterval, stoppingToken);

                try
                {
                    currentProcess.Refresh();
                    var currentCpuTime = currentProcess.TotalProcessorTime;
                    var currentTime = DateTime.UtcNow;

                    var cpuUsedMs = (currentCpuTime - _lastTotalProcessorTime).TotalMilliseconds;
                    var totalMsPassed = (currentTime - _lastMonitorTime).TotalMilliseconds;

                    var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                    var cpuUsagePercentage = Math.Round(cpuUsageTotal * 100, 2);

                    _lastTotalProcessorTime = currentCpuTime;
                    _lastMonitorTime = currentTime;

                    using var scope = _scopeFactory.CreateScope();
                    var dashboardManager = scope.ServiceProvider.GetRequiredService<IDashboardManager>();
                    var metricRepository = scope.ServiceProvider.GetRequiredService<ISystemMetricRepository>();

                    var feed = await dashboardManager.GetDashboardFeedAsync();

                    var metric = new SystemMetric
                    {
                        ActiveStreams = feed.ActiveStreamCount,
                        ActiveTranscodes = feed.ActiveTranscodeCount,
                        CpuUsagePercentage = cpuUsagePercentage
                    };

                    await metricRepository.AddMetricAsync(metric);

                    _logger.LogDebug($"Logged Pulse: {metric.ActiveStreams} Streams, {metric.ActiveTranscodes} Transcodes, CPU: {metric.CpuUsagePercentage}%");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while polling analytics.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Analytics Poller Service is stopping cleanly.");
        }
    }
}