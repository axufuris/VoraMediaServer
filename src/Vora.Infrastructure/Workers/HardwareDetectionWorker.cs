using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Infrastructure.Transcoding;

namespace Vora.Infrastructure.Workers;

public class HardwareDetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHardwareCapabilityService _hardware;
    private readonly ILogger<HardwareDetectionWorker> _logger;

    public HardwareDetectionWorker(IServiceScopeFactory scopeFactory, IHardwareCapabilityService hardware, ILogger<HardwareDetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hardware = hardware;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>().GetSettingsAsync();
            var resolved = settings.UseHardwareAcceleration
                ? await _hardware.ResolveAsync(settings.HardwareAccelerationType, settings.HardwareTranscodingDevice, stoppingToken)
                : null;

            if (resolved is null)
            {
                _logger.LogInformation("Hardware acceleration: none in use, so video transcodes on the CPU.");
            }
            else
            {
                _logger.LogInformation("Hardware acceleration: {Hardware} on {Device}.", HardwareArguments.Label(resolved.Type), resolved.Device ?? "the default GPU");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardware detection failed at startup; it runs again when a stream starts.");
        }
    }
}
