using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vora.Application.Settings;
using Vora.Application.Watchers;

namespace Vora.Infrastructure.FileSystem;

public class StartupWatcherService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupWatcherService> _logger;

    public StartupWatcherService(IServiceProvider serviceProvider, ILogger<StartupWatcherService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var remoteAccessManager = scope.ServiceProvider.GetRequiredService<IRemoteAccessManager>();
        try
        {
            await remoteAccessManager.BootUpnpMappingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Non-fatal error initializing UPnP port mapping at startup.");
        }

        var watcherService = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();
        await watcherService.RestartAllWatchersAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
