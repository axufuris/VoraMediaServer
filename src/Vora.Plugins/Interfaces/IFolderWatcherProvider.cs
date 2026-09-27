namespace Vora.Plugins.Interfaces;

public interface IFolderWatcherProvider : IVoraPlugin
{
    void StartWatching(Guid libraryId, IEnumerable<string> directories, int pollingInterval, Func<string, Task> onFileAdded, Func<string, Task> onFileDeleted);
    void StopWatching(Guid libraryId);
    bool IsWatching(Guid libraryId);
}
