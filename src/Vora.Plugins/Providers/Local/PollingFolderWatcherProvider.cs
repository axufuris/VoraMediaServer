using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class PollingFolderWatcherProvider : IFolderWatcherProvider, IDisposable
{
    public string Id => "polling_watcher";
    public string Name => "Polling Watcher";
    public string Version => "1.0.0";
    public string Description => "Universal file watcher. Periodically scans directories for changes. Safe for Docker and NAS network shares.";
    public bool IsSystemPlugin => true;
    public string Type => "FolderWatcher";
    public string DeveloperName => "Andy Xufuris";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Enumerable.Empty<PluginSettingDefinitionDto>();

    private const int DefaultPollingSeconds = 300;

    private readonly ConcurrentDictionary<Guid, List<string>> _watchedDirectories = new();
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _knownFiles = new();
    private readonly ConcurrentDictionary<Guid, (Func<string, Task> OnAdded, Func<string, Task> OnDeleted)> _callbacks = new();
    private readonly ConcurrentDictionary<Guid, Timer> _timers = new();
    private readonly ConcurrentDictionary<Guid, byte> _pollingLibraries = new();

    private readonly object _lock = new();

    private readonly ILogger<PollingFolderWatcherProvider> _logger;

    public PollingFolderWatcherProvider(ILogger<PollingFolderWatcherProvider> logger)
    {
        _logger = logger;
    }

    public void StartWatching(Guid libraryId, IEnumerable<string> directories, int pollingInterval, Func<string, Task> onFileAdded, Func<string, Task> onFileDeleted)
    {
        var paths = directories.Where(Directory.Exists).ToList();
        if (paths.Count == 0) return;

        var interval = TimeSpan.FromSeconds(pollingInterval > 0 ? pollingInterval : DefaultPollingSeconds);

        _watchedDirectories[libraryId] = paths;
        _knownFiles[libraryId] = GetCurrentFiles(paths).Files;
        _callbacks[libraryId] = (onFileAdded, onFileDeleted);

        lock (_lock)
        {
            if (_timers.TryRemove(libraryId, out var existing)) existing.Dispose();
            _timers[libraryId] = new Timer(PollLibrary, libraryId, TimeSpan.Zero, interval);
        }
    }

    public void StopWatching(Guid libraryId)
    {
        lock (_lock)
        {
            if (_timers.TryRemove(libraryId, out var timer)) timer.Dispose();
        }

        _watchedDirectories.TryRemove(libraryId, out _);
        _knownFiles.TryRemove(libraryId, out _);
        _callbacks.TryRemove(libraryId, out _);
        _pollingLibraries.TryRemove(libraryId, out _);
    }

    public bool IsWatching(Guid libraryId) => _watchedDirectories.ContainsKey(libraryId);

    private void PollLibrary(object? state)
    {
        if (state is not Guid libraryId) return;
        if (!_watchedDirectories.TryGetValue(libraryId, out var paths)) return;
        if (!_knownFiles.TryGetValue(libraryId, out var previousFiles) || !_callbacks.TryGetValue(libraryId, out var callbacks)) return;

        if (!_pollingLibraries.TryAdd(libraryId, 0)) return;
        try
        {
            var (currentFiles, complete) = GetCurrentFiles(paths);
            var changes = ResolveChanges(currentFiles, previousFiles, complete);

            _knownFiles[libraryId] = changes.Known;

            foreach (var file in changes.Added) _ = callbacks.OnAdded(file);
            foreach (var file in changes.Deleted) _ = callbacks.OnDeleted(file);
        }
        finally
        {
            _pollingLibraries.TryRemove(libraryId, out _);
        }
    }

    // A file missing from an INCOMPLETE listing has not been shown to be gone —
    // the share may simply have been unreachable this tick. Inferring deletion
    // there would queue a cleanup for every file in the library on one network
    // blip. (ProcessFileDeletedAsync re-checks File.Exists before acting, so the
    // rows survive, but the queue still fills with thousands of no-op tasks, and
    // a share that is genuinely unreachable answers "gone" to that check too.)
    //
    // Additions stay safe either way: a path that turned up really is there.
    // Known is unioned rather than replaced on an incomplete pass, so what we
    // could not see this time stays known and does not read as a deletion on the
    // next complete one.
    internal static (List<string> Added, List<string> Deleted, HashSet<string> Known) ResolveChanges(
        HashSet<string> currentFiles,
        HashSet<string> previousFiles,
        bool complete)
    {
        var added = currentFiles.Except(previousFiles).ToList();

        if (!complete)
        {
            var known = new HashSet<string>(currentFiles, StringComparer.OrdinalIgnoreCase);
            known.UnionWith(previousFiles);
            return (added, new List<string>(), known);
        }

        return (added, previousFiles.Except(currentFiles).ToList(), currentFiles);
    }

    private (HashSet<string> Files, bool Complete) GetCurrentFiles(List<string> directories)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var complete = true;

        foreach (var dir in directories)
        {
            foreach (var file in ResilientDirectory.EnumerateFiles(dir, (directory, ex) =>
            {
                complete = false;
                _logger.LogWarning(ex, "Could not read {Directory} while polling; treating this pass as incomplete.", directory);
            }))
            {
                files.Add(file);
            }
        }

        return (files, complete);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var timer in _timers.Values) timer.Dispose();
            _timers.Clear();
        }
    }
}
