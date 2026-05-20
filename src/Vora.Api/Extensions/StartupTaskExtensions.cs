using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Watchers;
using Vora.Infrastructure.FileSystem;

namespace Vora.Api.Extensions;

public static class StartupTaskExtensions
{
    public static async Task RunVoraStartupTasksAsync(this WebApplication app)
    {
        await InitializeFolderWatchersAsync(app);
        await PreloadIptvEpgCacheAsync(app);
    }

    private static async Task InitializeFolderWatchersAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var libraryRepo = scope.ServiceProvider.GetRequiredService<ILibraryRepository>();
            var folderWatcher = scope.ServiceProvider.GetRequiredService<IFolderWatcherService>();
            var libraries = await libraryRepo.GetAllLibrariesAsync();

            foreach (var library in libraries)
            {
                if (!library.EnableRealTimeWatching || library.FolderPaths == null || library.FolderPaths.Count == 0)
                {
                    continue;
                }

                folderWatcher.StartWatching(library.Id, library.FolderPaths);
                logger.LogInformation("Auto-started folder watching for library: {LibraryName}", library.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing folder watchers.");
        }
    }

    private static async Task PreloadIptvEpgCacheAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var epgService = scope.ServiceProvider.GetRequiredService<IIptvEpgService>();
        await epgService.LoadCacheIntoMemoryAsync();
    }
}
