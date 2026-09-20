using Microsoft.EntityFrameworkCore;
using Vora.Application.Iptv;
using Vora.Application.Libraries;
using Vora.Application.Plugins;
using Vora.Application.Watchers;
using Vora.Infrastructure.FileSystem;
using Vora.Infrastructure.Persistence;

namespace Vora.Api.Extensions;

public static class StartupTaskExtensions
{
    public static async Task RunVoraStartupTasksAsync(this WebApplication app)
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        await MigrateDatabaseAsync(app);
        await SeedPluginSettingsFromEnvironmentAsync(app);
        await InitializeFolderWatchersAsync(app);
        await PreloadIptvEpgCacheAsync(app);
    }

    private static async Task SeedPluginSettingsFromEnvironmentAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IPluginSettingsEnvSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed plugin settings from environment.");
        }
    }

    private static async Task MigrateDatabaseAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var db = scope.ServiceProvider.GetRequiredService<VoraDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation("Database schema is up to date.");
                return;
            }

            logger.LogInformation("Applying {Count} pending database migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Database migration failed. Aborting startup.");
            throw;
        }
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
