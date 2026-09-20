using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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

    private readonly ILogger<NativeFolderWatcherProvider> _logger;
    private readonly ConcurrentDictionary<Guid, List<FileSystemWatcher>> _watchers = new();

    public NativeFolderWatcherProvider(ILogger<NativeFolderWatcherProvider> logger)
    {
        _logger = logger;
    }

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

            watcher.Created += async (s, e) => await SafeInvokeAsync(onFileAdded, e.FullPath, "Created");
            watcher.Renamed += async (s, e) => await SafeInvokeAsync(onFileAdded, e.FullPath, "Renamed");
            watcher.Deleted += async (s, e) => await SafeInvokeAsync(onFileDeleted, e.FullPath, "Deleted");

            libraryWatchers.Add(watcher);
        }

        if (libraryWatchers.Any()) _watchers.TryAdd(libraryId, libraryWatchers);
    }

    private async Task SafeInvokeAsync(Func<string, Task> handler, string path, string changeType)
    {
        try
        {
            await handler(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Folder watcher {ChangeType} handler failed for {Path}", changeType, path);
        }
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
