using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.FileSystem;

public class FolderWatcherService : IFolderWatcherService
{
    private static readonly string[] SupportedExtensions = { ".mkv", ".mp4", ".avi", ".m4v" };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FolderWatcherService> _logger;
    private readonly IEnumerable<IFolderWatcherProvider> _providers;
    private readonly ConcurrentDictionary<Guid, IFolderWatcherProvider> _activeWatchers = new();

    public FolderWatcherService(IServiceProvider serviceProvider, ILogger<FolderWatcherService> logger, IEnumerable<IFolderWatcherProvider> providers)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _providers = providers;
    }

    public void StartWatching(Guid libraryId, IEnumerable<string> directoryPaths)
    {
        if (_activeWatchers.ContainsKey(libraryId)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var settingsRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingsRepository>();
                var settings = await settingsRepo.GetSettingsAsync();

                var providerId = settings.FolderWatcherProviderId;
                var interval = settings.FolderWatcherPollingInterval;

                var provider = _providers.FirstOrDefault(p => p.Id == providerId) ?? _providers.FirstOrDefault(p => p.Id == "polling_watcher");
                if (provider == null) return;

                _logger.LogInformation("Starting {ProviderName} for Library {LibraryId}", provider.Name, libraryId);

                provider.StartWatching(
                    libraryId,
                    directoryPaths,
                    interval,
                    async (filePath) => await ProcessFileAddedAsync(libraryId, filePath),
                    async (filePath) => await ProcessFileDeletedAsync(filePath)
                );

                _activeWatchers.TryAdd(libraryId, provider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start folder watcher for library {LibraryId}.", libraryId);
            }
        });
    }

    public void StopWatching(Guid libraryId)
    {
        if (_activeWatchers.TryRemove(libraryId, out var provider))
        {
            provider.StopWatching(libraryId);
        }
    }

    public bool IsWatching(Guid libraryId) => _activeWatchers.ContainsKey(libraryId);

    public async Task RestartAllWatchersAsync()
    {
        _logger.LogInformation("Restarting all Folder Watchers...");

        foreach (var libraryId in _activeWatchers.Keys.ToList())
        {
            StopWatching(libraryId);
        }

        using var scope = _serviceProvider.CreateScope();
        var libraryRepo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();

        var activeLibraries = await libraryRepo.GetAllProjectedAsync(l => new { l.Id, l.FolderPaths, l.EnableRealTimeWatching });

        foreach (var lib in activeLibraries.Where(l => l.EnableRealTimeWatching))
        {
            StartWatching(lib.Id, lib.FolderPaths);
        }
    }

    private async Task ProcessFileAddedAsync(Guid libraryId, string filePath)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return;

        await Task.Delay(5000);
        _logger.LogInformation("New media detected: {FilePath}. Triggering ingestion pipeline.", filePath);

        using var scope = _serviceProvider.CreateScope();
        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        taskQueue.QueueScanLibrary(libraryId);
    }

    private async Task ProcessFileDeletedAsync(string filePath)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return;

        await Task.Yield();
        using var scope = _serviceProvider.CreateScope();
        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        taskQueue.QueueRemoveOrphanedMedia(filePath);
    }
}
