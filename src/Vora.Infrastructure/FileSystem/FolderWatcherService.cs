using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vora.Application.Libraries;
using Vora.Application.Media;
using Vora.Application.Settings;
using Vora.Application.Tasks;
using Vora.Application.Watchers;
using Vora.Domain.Enums;
using Vora.Plugins;
using Vora.Plugins.Interfaces;

namespace Vora.Infrastructure.FileSystem;

public class FolderWatcherService : IFolderWatcherService
{
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

                var libraryManager = scope.ServiceProvider.GetRequiredService<ILibraryManager>();
                var libraryName = (await libraryManager.GetLibraryByIdAsync(libraryId))?.Name ?? libraryId.ToString();

                _logger.LogInformation("Starting {ProviderName} for library {LibraryName}", provider.Name, libraryName);

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
        // Cheap reject of the non-media noise (.nfo, .srt, artwork) before paying
        // for the settle delay and a DI scope. The library's own type decides which
        // of the two sets actually applies, once it is known.
        if (!MediaFileExtensions.IsMedia(filePath)) return;

        await Task.Delay(5000);

        using var scope = _serviceProvider.CreateScope();

        var library = await GetWatchTargetAsync(scope, libraryId);
        if (library == null) return;

        // A video file dropped into a music library (or the reverse) is not ours to
        // ingest — the single-file scanners are dispatched by library type and would
        // parse it as the wrong kind of media.
        if (!IsSupportedFor(filePath, library.Type)) return;

        // Honor the library's exclude filters here so excluded files (e.g. a
        // *.TDARR copy still transcoding) don't even queue a scan task — the
        // scanner would reject them anyway, but this keeps them off the task list.
        if (MatchesExcludeFilter(Path.GetFileName(filePath), library.ExcludeFilters))
        {
            _logger.LogInformation("Skipping excluded file {FilePath}.", filePath);
            return;
        }

        _logger.LogInformation("New media detected: {FilePath}. Triggering single-file ingestion.", filePath);
        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        QueueSingleFileScan(taskQueue, library.Type, libraryId, filePath);
    }

    private async Task ProcessFileDeletedAsync(Guid libraryId, string filePath)
    {
        if (!MediaFileExtensions.IsMedia(filePath)) return;

        // A rename/move (e.g. an import upgrading a release in place) surfaces as a
        // delete immediately followed by a create. Let it settle and re-check: if
        // the file is back, this was churn, not a real deletion — don't trash it.
        await Task.Delay(5000);
        if (File.Exists(filePath)) return;

        using var scope = _serviceProvider.CreateScope();

        var library = await GetWatchTargetAsync(scope, libraryId);
        if (library == null) return;
        if (!IsSupportedFor(filePath, library.Type)) return;

        // Excluded files (e.g. *.TDARR temp copies) were never ingested, so a
        // deletion must not queue an orphan-cleanup task for them.
        if (MatchesExcludeFilter(Path.GetFileName(filePath), library.ExcludeFilters)) return;

        var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();
        taskQueue.QueueRemoveOrphanedMedia(filePath);
    }

    private static void QueueSingleFileScan(ITaskQueueManager taskQueue, LibraryType type, Guid libraryId, string filePath)
    {
        if (type == LibraryType.Music)
            taskQueue.QueueScanNewMusicFile(libraryId, filePath);
        else
            taskQueue.QueueScanNewFile(libraryId, filePath);
    }

    private async Task ReconcileLibraryAsync(Guid libraryId, IEnumerable<string> directoryPaths)
    {
        try
        {
            var paths = directoryPaths.Where(Directory.Exists).ToList();
            if (paths.Count == 0) return;

            using var scope = _serviceProvider.CreateScope();
            var mediaRepo = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
            var taskQueue = scope.ServiceProvider.GetRequiredService<ITaskQueueManager>();

            var target = await GetWatchTargetAsync(scope, libraryId);
            if (target == null) return;

            var ingested = await mediaRepo.GetExistingLibraryPathsAsync(libraryId);

            var filesOnDisk = paths.SelectMany(EnumerateSupportedFiles);
            var uningested = FindUningestedFiles(filesOnDisk, ingested, target.ExcludeFilters, target.Type);
            if (uningested.Count == 0) return;

            _logger.LogInformation(
                "Watcher reconciliation for library {LibraryName} found {Count} file(s) on disk that were never ingested; queueing them.",
                target.Name, uningested.Count);

            foreach (var filePath in uningested)
            {
                QueueSingleFileScan(taskQueue, target.Type, libraryId, filePath);
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

    internal static List<string> FindUningestedFiles(IEnumerable<string> filesOnDisk, ISet<string> ingestedPaths, IReadOnlyList<string> excludeFilters, LibraryType libraryType)
    {
        var supported = ExtensionsFor(libraryType);
        var result = new List<string>();
        foreach (var file in filesOnDisk)
        {
            if (!supported.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            if (ingestedPaths.Contains(file)) continue;
            if (MatchesExcludeFilter(Path.GetFileName(file), excludeFilters)) continue;
            result.Add(file);
        }
        return result;
    }

    // Which of the two shared sets applies is the library's type, not the file's:
    // the single-file scanners are dispatched by type, so a stray .mp3 in a movie
    // library would otherwise be handed to the movie parser.
    internal static IReadOnlyList<string> ExtensionsFor(LibraryType libraryType) =>
        libraryType == LibraryType.Music ? MediaFileExtensions.Audio : MediaFileExtensions.Video;

    private static bool IsSupportedFor(string filePath, LibraryType libraryType) =>
        ExtensionsFor(libraryType).Contains(Path.GetExtension(filePath).ToLowerInvariant());

    private sealed record WatchTarget(string Name, LibraryType Type, List<string> ExcludeFilters);

    private static async Task<WatchTarget?> GetWatchTargetAsync(IServiceScope scope, Guid libraryId)
    {
        var libraryRepo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
        var projected = await libraryRepo.GetProjectedByIdAsync(libraryId, l => new { l.Name, l.Type, l.ExcludeFilters });
        return projected == null ? null : new WatchTarget(projected.Name, projected.Type, projected.ExcludeFilters);
    }

    private static bool MatchesExcludeFilter(string fileName, IReadOnlyList<string>? excludeFilters)
    {
        return excludeFilters != null
            && excludeFilters.Any(f => !string.IsNullOrWhiteSpace(f) && fileName.Contains(f.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
