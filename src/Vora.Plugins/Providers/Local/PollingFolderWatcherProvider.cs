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

    private readonly ConcurrentDictionary<Guid, List<string>> _watchedDirectories = new();
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _knownFiles = new();
    private readonly ConcurrentDictionary<Guid, (Func<string, Task> OnAdded, Func<string, Task> OnDeleted)> _callbacks = new();

    private Timer? _pollingTimer;
    private bool _isPolling = false;
    private readonly object _lock = new();

    public void StartWatching(Guid libraryId, IEnumerable<string> directories, int pollingInterval, Func<string, Task> onFileAdded, Func<string, Task> onFileDeleted)
    {
        var paths = directories.Where(Directory.Exists).ToList();
        if (!paths.Any()) return;

        _watchedDirectories.TryAdd(libraryId, paths);
        _knownFiles.TryAdd(libraryId, GetCurrentFiles(paths));
        _callbacks.TryAdd(libraryId, (onFileAdded, onFileDeleted));

        if (_pollingTimer == null)
        {
            _pollingTimer = new Timer(PollDirectories, null, TimeSpan.Zero, TimeSpan.FromSeconds(300));
        }
        else
        {
            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(pollingInterval));
        }
    }

    public void StopWatching(Guid libraryId)
    {
        _watchedDirectories.TryRemove(libraryId, out _);
        _knownFiles.TryRemove(libraryId, out _);
        _callbacks.TryRemove(libraryId, out _);

        if (_watchedDirectories.IsEmpty)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }
    }

    public bool IsWatching(Guid libraryId) => _watchedDirectories.ContainsKey(libraryId);

    private void PollDirectories(object? state)
    {
        lock (_lock)
        {
            if (_isPolling) return;
            _isPolling = true;
        }

        try
        {
            foreach (var kvp in _watchedDirectories)
            {
                var libraryId = kvp.Key;
                if (!_knownFiles.TryGetValue(libraryId, out var previousFiles) || !_callbacks.TryGetValue(libraryId, out var callbacks)) continue;

                var currentFiles = GetCurrentFiles(kvp.Value);
                var addedFiles = currentFiles.Except(previousFiles).ToList();
                var deletedFiles = previousFiles.Except(currentFiles).ToList();

                _knownFiles[libraryId] = currentFiles;

                foreach (var file in addedFiles) _ = callbacks.OnAdded(file);
                foreach (var file in deletedFiles) _ = callbacks.OnDeleted(file);
            }
        }
        finally
        {
            lock (_lock) _isPolling = false;
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

    public void Dispose() => _pollingTimer?.Dispose();
}
