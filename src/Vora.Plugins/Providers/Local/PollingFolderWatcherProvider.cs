using System.Collections.Concurrent;
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

    public void StartWatching(Guid libraryId, IEnumerable<string> directories, int pollingInterval, Func<string, Task> onFileAdded, Func<string, Task> onFileDeleted)
    {
        var paths = directories.Where(Directory.Exists).ToList();
        if (paths.Count == 0) return;

        var interval = TimeSpan.FromSeconds(pollingInterval > 0 ? pollingInterval : DefaultPollingSeconds);

        _watchedDirectories[libraryId] = paths;
        _knownFiles[libraryId] = GetCurrentFiles(paths);
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
            var currentFiles = GetCurrentFiles(paths);
            var addedFiles = currentFiles.Except(previousFiles).ToList();
            var deletedFiles = previousFiles.Except(currentFiles).ToList();

            _knownFiles[libraryId] = currentFiles;

            foreach (var file in addedFiles) _ = callbacks.OnAdded(file);
            foreach (var file in deletedFiles) _ = callbacks.OnDeleted(file);
        }
        finally
        {
            _pollingLibraries.TryRemove(libraryId, out _);
        }
    }

    private HashSet<string> GetCurrentFiles(List<string> directories)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in directories)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    files.Add(f);
                }
            }
            catch { }
        }
        return files;
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
