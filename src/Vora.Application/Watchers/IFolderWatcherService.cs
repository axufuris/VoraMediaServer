namespace Vora.Application.Watchers;

public interface IFolderWatcherService
{
    void StartWatching(Guid libraryId, IEnumerable<string> directoryPaths);
    void StopWatching(Guid libraryId);
    bool IsWatching(Guid libraryId);
    Task RestartAllWatchersAsync();
}
