using System.Collections.Concurrent;
using Vora.Plugins.Dtos;
using Vora.Plugins.Interfaces;

namespace Vora.Plugins.Providers.Local;

public class NativeFolderWatcherProvider : IFolderWatcherProvider, IDisposable
{
    public string Id => "native_watcher";
    public string Name => "Native OS Watcher";
    public string Version => "1.0.0";
    public string Description => "Extremely fast, zero-overhead file watcher. Requires local drives (Does NOT work over network shares in Docker).";
    public bool IsSystemPlugin => true;
    public string Type => "FolderWatcher";
    public string DeveloperName => "Andy Xufuris";

    public IEnumerable<PluginSettingDefinitionDto> GetSettingDefinitions() => Enumerable.Empty<PluginSettingDefinitionDto>();

    private readonly ConcurrentDictionary<Guid, List<FileSystemWatcher>> _watchers = new();

    public void StartWatching(Guid libraryId, IEnumerable<string> directories, int pollingInterval, Func<string, Task> onFileAdded, Func<string, Task> onFileDeleted)
    {
        if (_watchers.ContainsKey(libraryId)) return;

        var libraryWatchers = new List<FileSystemWatcher>();

        foreach (var dir in directories.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                Filter = "*.*",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += async (s, e) => await onFileAdded(e.FullPath);
            watcher.Renamed += async (s, e) => await onFileAdded(e.FullPath);
            watcher.Deleted += async (s, e) => await onFileDeleted(e.FullPath);

            libraryWatchers.Add(watcher);
        }

        if (libraryWatchers.Any()) _watchers.TryAdd(libraryId, libraryWatchers);
    }

    public void StopWatching(Guid libraryId)
    {
        if (_watchers.TryRemove(libraryId, out var watcherList))
        {
            foreach (var watcher in watcherList)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }
    }

    public bool IsWatching(Guid libraryId) => _watchers.ContainsKey(libraryId);

    public void Dispose()
    {
        foreach (var watcherList in _watchers.Values)
        {
            foreach (var watcher in watcherList) watcher.Dispose();
        }
        _watchers.Clear();
    }
}
