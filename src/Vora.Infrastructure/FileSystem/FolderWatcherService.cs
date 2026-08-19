using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Media;
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
                    async (filePath) => await ProcessFileDeletedAsync(libraryId, filePath)
                );

                _activeWatchers.TryAdd(libraryId, provider);

                // The provider only reports files that appear AFTER it starts, and
                // it treats everything already on disk as known. So a file that was
                // on disk but never ingested — a scan that arrived between runs, an
                // ingest that failed, or anything present at a restart — would stay
                // invisible forever (there is no nightly scan to catch it). Reconcile
                // against the database now so any un-ingested file gets queued.
                await ReconcileLibraryAsync(libraryId, directoryPaths);
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

        using var scope = _serviceProvider.CreateScope();

        // Honor the library's exclude filters here so excluded files (e.g. a
        // *.TDARR copy still transcoding) don't even queue a scan task — the
        // scanner would reject them anyway, but this keeps them off the task list.
        if (await IsExcludedAsync(scope, libraryId, Path.GetFileName(filePath)))
        {
            _logger.LogInformation("Skipping excluded file {FilePath}.", filePath);
            return;
        }

        _logger.LogInformation("New media detected: {FilePath}. Triggering single-file ingestion.", filePath);
        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        taskQueue.QueueScanNewFile(libraryId, filePath);
    }

    private async Task ProcessFileDeletedAsync(Guid libraryId, string filePath)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant())) return;

        using var scope = _serviceProvider.CreateScope();

        // Excluded files (e.g. *.TDARR temp copies) were never ingested, so a
        // deletion must not queue an orphan-cleanup task for them.
        if (await IsExcludedAsync(scope, libraryId, Path.GetFileName(filePath)))
        {
            return;
        }

        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        taskQueue.QueueRemoveOrphanedMedia(filePath);
    }

    private async Task ReconcileLibraryAsync(Guid libraryId, IEnumerable<string> directoryPaths)
    {
        try
        {
            var paths = directoryPaths.Where(Directory.Exists).ToList();
            if (paths.Count == 0) return;

            using var scope = _serviceProvider.CreateScope();
            var mediaRepo = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
            var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();
            var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();

            var ingested = await mediaRepo.GetExistingLibraryPathsAsync(libraryId);
            var library = await libraryManager.GetLibraryByIdAsync(libraryId);
            var excludeFilters = library?.ExcludeFilters ?? new List<string>();

            var filesOnDisk = paths.SelectMany(EnumerateSupportedFiles);
            var uningested = FindUningestedFiles(filesOnDisk, ingested, excludeFilters);
            if (uningested.Count == 0) return;

            _logger.LogInformation(
                "Watcher reconciliation for library {LibraryId} found {Count} file(s) on disk that were never ingested; queueing them.",
                libraryId, uningested.Count);

            foreach (var filePath in uningested)
            {
                taskQueue.QueueScanNewFile(libraryId, filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder watcher reconciliation failed for library {LibraryId}.", libraryId);
        }
    }

    private static IEnumerable<string> EnumerateSupportedFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    internal static List<string> FindUningestedFiles(IEnumerable<string> filesOnDisk, ISet<string> ingestedPaths, IReadOnlyList<string> excludeFilters)
    {
        var result = new List<string>();
        foreach (var file in filesOnDisk)
        {
            if (!SupportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            if (ingestedPaths.Contains(file)) continue;
            if (MatchesExcludeFilter(Path.GetFileName(file), excludeFilters)) continue;
            result.Add(file);
        }
        return result;
    }

    private static async Task<bool> IsExcludedAsync(IServiceScope scope, Guid libraryId, string fileName)
    {
        var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();
        var library = await libraryManager.GetLibraryByIdAsync(libraryId);
        return MatchesExcludeFilter(fileName, library?.ExcludeFilters);
    }

    private static bool MatchesExcludeFilter(string fileName, IReadOnlyList<string>? excludeFilters)
    {
        return excludeFilters != null
            && excludeFilters.Any(f => !string.IsNullOrWhiteSpace(f) && fileName.Contains(f.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
